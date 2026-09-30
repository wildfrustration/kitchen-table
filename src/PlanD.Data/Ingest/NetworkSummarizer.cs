using System.Collections.Concurrent;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace PlanD.Data.Ingest;

/// <summary>
/// Streams the pharmacy network files (≈300M rows) and keeps, per PDP and pharmacy type, the pharmacy
/// counts and the most common fee schedule. Unit costs in the pricing file are plan-level averages over
/// in-area retail pharmacies, so the typical in-area fee is the matching dispensing fee for a quote.
/// </summary>
internal sealed class NetworkSummarizer(NpgsqlDataSource db, Action<string> log)
{
    private static readonly string[] TypeNames = ["pref", "std", "mail_pref", "mail_std"];

    // FLOOR_PRICE precedes these in the quarterly files; the monthly files don't have it.
    private static readonly string[] FeeColumns =
    [
        "BRAND_DISPENSING_FEE_30", "BRAND_DISPENSING_FEE_60", "BRAND_DISPENSING_FEE_90",
        "GENERIC_DISPENSING_FEE_30", "GENERIC_DISPENSING_FEE_60", "GENERIC_DISPENSING_FEE_90",
        "SELECTED_DISPENSING_FEE_30", "SELECTED_DISPENSING_FEE_60", "SELECTED_DISPENSING_FEE_90",
    ];

    public async Task<long> LoadAsync(int releaseId, IReadOnlyList<string> parts, CancellationToken ct)
    {
        var results = new ConcurrentBag<Dictionary<string, TypeAgg[]>>();
        long rows = 0;
        await Parallel.ForEachAsync(parts, new ParallelOptions { MaxDegreeOfParallelism = parts.Count, CancellationToken = ct },
            (part, _) =>
            {
                var (plans, n) = SummarizePart(part);
                results.Add(plans);
                Interlocked.Add(ref rows, n);
                log($"  pharmacy: {Path.GetFileName(part)}: {n:N0} rows");
                return ValueTask.CompletedTask;
            });

        var merged = Merge(results);
        log($"  pharmacy: {rows:N0} rows across {merged.Count:N0} plans");
        return await Write(releaseId, merged);
    }

    private static (Dictionary<string, TypeAgg[]> Plans, long Rows) SummarizePart(string path)
    {
        using var r = new DelimitedReader(path, '|');
        int contract = r.Column("CONTRACT_ID"), segment = r.Column("SEGMENT_ID"),
            prefRetail = r.Column("PREFERRED_STATUS_RETAIL"), prefMail = r.Column("PREFERRED_STATUS_MAIL"),
            retail = r.Column("PHARMACY_RETAIL"), mail = r.Column("PHARMACY_MAIL"), inArea = r.Column("IN_AREA_FLAG");
        var fees = FeeColumns.Select(c => r.Column(c, c.Replace("SELECTED_", "SELECTED_DRUG_"))).ToArray();
        for (var i = 1; i < fees.Length; i++)
            if (fees[i] != fees[0] + i)
                throw new InvalidDataException($"{Path.GetFileName(path)}: fee columns are not in the expected order.");
        var floor = r.OptionalColumn("FLOOR_PRICE");
        if (floor is not null && floor != fees[0] - 1)
            throw new InvalidDataException($"{Path.GetFileName(path)}: FLOOR_PRICE is not just before the fee columns.");
        var first = floor ?? fees[0];
        if (r.OptionalColumn("PLAN_ID") != contract + 1 || segment != contract + 2)
            throw new InvalidDataException($"{Path.GetFileName(path)}: plan key columns are not adjacent.");

        var plans = new Dictionary<string, TypeAgg[]>();
        var lookup = plans.GetAlternateLookup<ReadOnlySpan<char>>();
        long n = 0;
        while (r.Read())
        {
            n++;
            if (!SpufLoader.IsPdp(r.Span(contract))) continue;
            var key = r.Range(contract, segment);
            if (!lookup.TryGetValue(key, out var types))
            {
                types = [new(), new(), new(), new()];
                lookup[key] = types;
            }

            var feeSpan = r.Range(first, fees[^1]);
            var local = r.Span(inArea) is "1";
            if (r.Flag(retail)) types[r.Flag(prefRetail) ? 0 : 1].Add(feeSpan, local);
            if (r.Flag(mail)) types[r.Flag(prefMail) ? 2 : 3].Add(feeSpan, local);
        }
        return (plans, n);
    }

    private static Dictionary<string, TypeAgg[]> Merge(IEnumerable<Dictionary<string, TypeAgg[]>> parts)
    {
        var merged = new Dictionary<string, TypeAgg[]>();
        foreach (var part in parts)
        foreach (var (key, types) in part)
        {
            if (!merged.TryGetValue(key, out var target))
            {
                merged[key] = types;
                continue;
            }
            for (var i = 0; i < types.Length; i++) target[i].Merge(types[i]);
        }
        return merged;
    }

    private async Task<long> Write(int releaseId, Dictionary<string, TypeAgg[]> plans)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var w = await conn.BeginBinaryImportAsync("""
            copy cms.spuf_network_summary (release_id, contract_id, plan_id, segment_id, pharmacy_type, pharmacy_count, in_area_count,
                brand_fee_30, brand_fee_60, brand_fee_90, generic_fee_30, generic_fee_60, generic_fee_90,
                selected_fee_30, selected_fee_60, selected_fee_90, floor_price)
            from stdin (format binary)
            """);
        long n = 0;
        foreach (var (key, types) in plans)
        {
            var k = key.Split('|');
            for (var t = 0; t < types.Length; t++)
            {
                var agg = types[t];
                if (agg.Count == 0) continue;
                var f = agg.TypicalFees().Split('|').Select(Parse).ToArray();
                if (f.Length == FeeColumns.Length) f = [null, .. f]; // no floor price column

                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(k[0]); w.WriteText(k[1]); w.WriteText(k[2]);
                w.WriteText(TypeNames[t]);
                w.Write(agg.Count, NpgsqlDbType.Integer);
                w.Write(agg.InAreaCount, NpgsqlDbType.Integer);
                for (var i = 1; i < f.Length; i++) w.WriteNumeric(f[i]);
                w.WriteNumeric(f[0]);
                n++;
            }
        }
        await w.CompleteAsync();
        return n;
    }

    private static decimal? Parse(string value)
    {
        var s = value.Trim();
        return s is "" or "." ? null : decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Pharmacy counts and a fee-schedule histogram for one plan and pharmacy type.</summary>
    private sealed class TypeAgg
    {
        public int Count;
        public int InAreaCount;
        private readonly Dictionary<string, int> _all = [];
        private readonly Dictionary<string, int> _inArea = [];

        public void Add(ReadOnlySpan<char> fees, bool inArea)
        {
            Count++;
            Bump(_all, fees);
            if (!inArea) return;
            InAreaCount++;
            Bump(_inArea, fees);
        }

        private static void Bump(Dictionary<string, int> histogram, ReadOnlySpan<char> fees)
        {
            var lookup = histogram.GetAlternateLookup<ReadOnlySpan<char>>();
            lookup[fees] = lookup.TryGetValue(fees, out var c) ? c + 1 : 1;
        }

        public void Merge(TypeAgg other)
        {
            Count += other.Count;
            InAreaCount += other.InAreaCount;
            foreach (var (k, v) in other._all) _all[k] = _all.GetValueOrDefault(k) + v;
            foreach (var (k, v) in other._inArea) _inArea[k] = _inArea.GetValueOrDefault(k) + v;
        }

        /// <summary>The most common fee schedule among in-area pharmacies, else among all of them.</summary>
        public string TypicalFees() =>
            (_inArea.Count > 0 ? _inArea : _all).MaxBy(kv => kv.Value).Key;
    }
}
