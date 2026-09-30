using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Npgsql;
using NpgsqlTypes;

namespace PlanD.Data.Ingest;

/// <summary>
/// Streams the pharmacy network files (≈300M rows, two passes) for the PDPs:
/// <list type="number">
/// <item>Pass 1: per plan and pharmacy type, the pharmacy counts and most common fee schedule (a quote with no named
/// pharmacy uses those), plus a 128-bit fingerprint of the plan's whole network.</item>
/// <item>Pass 2: the rows of one plan per distinct fingerprint, so plans with identical networks share one copy.</item>
/// </list>
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

    [Flags]
    private enum NetworkFlags : short
    {
        Retail = 1,
        Mail = 2,
        PreferredRetail = 4,
        PreferredMail = 8,
        InArea = 16,
    }

    public sealed record Counts(long Summaries, long Networks, long NetworkRows, long FeeSchedules);

    private int _nextFeeId;

    public async Task<Counts> LoadAsync(int releaseId, IReadOnlyList<string> parts, CancellationToken ct)
    {
        // Pass 1
        var results = new ConcurrentBag<Dictionary<string, PlanAgg>>();
        long rows = 0;
        await Parallel.ForEachAsync(parts, new ParallelOptions { MaxDegreeOfParallelism = parts.Count, CancellationToken = ct },
            (part, _) =>
            {
                var (plans, n) = SummarizePart(part);
                results.Add(plans);
                Interlocked.Add(ref rows, n);
                log($"  pharmacy pass 1: {Path.GetFileName(part)}: {n:N0} rows");
                return ValueTask.CompletedTask;
            });
        var merged = Merge(results);
        var summaries = await WriteSummaries(releaseId, merged);

        // Plans with the same fingerprint share a network; the first plan of each is its representative.
        var networkOf = new Dictionary<string, int>();
        var representatives = new Dictionary<string, int>();
        var byFingerprint = new Dictionary<(UInt128, UInt128, long), int>();
        foreach (var (plan, agg) in merged.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var fingerprint = (agg.Sum, agg.Xor, agg.Rows);
            if (!byFingerprint.TryGetValue(fingerprint, out var id))
            {
                id = byFingerprint.Count + 1;
                byFingerprint[fingerprint] = id;
                representatives[plan] = id;
            }
            networkOf[plan] = id;
        }
        log($"  pharmacy: {rows:N0} rows, {merged.Count:N0} PDPs, {byFingerprint.Count:N0} distinct networks");
        await WritePlanNetworks(releaseId, networkOf);

        // Pass 2
        var fees = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        long networkRows = 0;
        await Parallel.ForEachAsync(parts, new ParallelOptions { MaxDegreeOfParallelism = parts.Count, CancellationToken = ct },
            async (part, token) =>
            {
                var n = await WriteNetworkRows(releaseId, part, representatives, fees, token);
                Interlocked.Add(ref networkRows, n);
                log($"  pharmacy pass 2: {Path.GetFileName(part)}: {n:N0} network rows");
            });
        await WriteFeeSchedules(releaseId, fees);

        return new Counts(summaries, byFingerprint.Count, networkRows, fees.Count);
    }

    private sealed record Columns(int Contract, int Segment, int Npi, int Zip, int PrefRetail, int PrefMail, int Retail, int Mail, int InArea, int FeeStart, int FeeEnd)
    {
        public static Columns From(DelimitedReader r)
        {
            int contract = r.Column("CONTRACT_ID"), segment = r.Column("SEGMENT_ID");
            var fees = FeeColumns.Select(c => r.Column(c, c.Replace("SELECTED_", "SELECTED_DRUG_"))).ToArray();
            for (var i = 1; i < fees.Length; i++)
                if (fees[i] != fees[0] + i)
                    throw new InvalidDataException($"{Path.GetFileName(r.Path)}: fee columns are not in the expected order.");
            var floor = r.OptionalColumn("FLOOR_PRICE");
            if (floor is not null && floor != fees[0] - 1)
                throw new InvalidDataException($"{Path.GetFileName(r.Path)}: FLOOR_PRICE is not just before the fee columns.");
            if (r.OptionalColumn("PLAN_ID") != contract + 1 || segment != contract + 2)
                throw new InvalidDataException($"{Path.GetFileName(r.Path)}: plan key columns are not adjacent.");
            return new Columns(contract, segment, r.Column("PHARMACY_NUMBER"), r.Column("PHARMACY_ZIPCODE"),
                r.Column("PREFERRED_STATUS_RETAIL"), r.Column("PREFERRED_STATUS_MAIL"), r.Column("PHARMACY_RETAIL"),
                r.Column("PHARMACY_MAIL"), r.Column("IN_AREA_FLAG"), floor ?? fees[0], fees[^1]);
        }
    }

    private static NetworkFlags Flags(DelimitedReader r, Columns c)
    {
        NetworkFlags f = 0;
        if (r.Flag(c.Retail)) f |= NetworkFlags.Retail;
        if (r.Flag(c.Mail)) f |= NetworkFlags.Mail;
        if (r.Flag(c.PrefRetail)) f |= NetworkFlags.PreferredRetail;
        if (r.Flag(c.PrefMail)) f |= NetworkFlags.PreferredMail;
        if (r.Span(c.InArea) is "1") f |= NetworkFlags.InArea;
        return f;
    }

    private static (Dictionary<string, PlanAgg> Plans, long Rows) SummarizePart(string path)
    {
        using var r = new DelimitedReader(path, '|');
        var c = Columns.From(r);
        var plans = new Dictionary<string, PlanAgg>();
        var lookup = plans.GetAlternateLookup<ReadOnlySpan<char>>();
        long n = 0;
        while (r.Read())
        {
            n++;
            if (!SpufLoader.IsPdp(r.Span(c.Contract))) continue;
            var key = r.Range(c.Contract, c.Segment);
            if (!lookup.TryGetValue(key, out var agg))
            {
                agg = new PlanAgg();
                lookup[key] = agg;
            }

            // Everything after the plan key except IN_AREA_FLAG, which depends on the plan's region, not its network.
            agg.AddToFingerprint(r.Range(c.Npi, c.Mail), r.Range(c.FeeStart, c.FeeEnd));

            var feeSpan = r.Range(c.FeeStart, c.FeeEnd);
            var flags = Flags(r, c);
            var local = flags.HasFlag(NetworkFlags.InArea);
            if (flags.HasFlag(NetworkFlags.Retail)) agg.Types[flags.HasFlag(NetworkFlags.PreferredRetail) ? 0 : 1].Add(feeSpan, local);
            if (flags.HasFlag(NetworkFlags.Mail)) agg.Types[flags.HasFlag(NetworkFlags.PreferredMail) ? 2 : 3].Add(feeSpan, local);
        }
        return (plans, n);
    }

    private static Dictionary<string, PlanAgg> Merge(IEnumerable<Dictionary<string, PlanAgg>> parts)
    {
        // A plan's rows can continue from one part file into the next; every part of the aggregate is order-independent.
        var merged = new Dictionary<string, PlanAgg>();
        foreach (var part in parts)
        foreach (var (key, agg) in part)
        {
            if (merged.TryGetValue(key, out var target)) target.Merge(agg);
            else merged[key] = agg;
        }
        return merged;
    }

    private async Task<long> WriteNetworkRows(int releaseId, string path, IReadOnlyDictionary<string, int> representatives,
        ConcurrentDictionary<string, int> fees, CancellationToken ct)
    {
        var lookup = new Dictionary<string, int>(representatives).GetAlternateLookup<ReadOnlySpan<char>>();
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var w = await conn.BeginBinaryImportAsync(
            "copy cms.spuf_network_pharmacy (release_id, npi, network_id, flags, fee_schedule_id, zip) from stdin (format binary)", ct);
        long n = 0;
        using (var r = new DelimitedReader(path, '|'))
        {
            var c = Columns.From(r);
            while (r.Read())
            {
                if (!SpufLoader.IsPdp(r.Span(c.Contract)) || !lookup.TryGetValue(r.Range(c.Contract, c.Segment), out var network)) continue;

                var feeId = fees.GetOrAdd(r.Range(c.FeeStart, c.FeeEnd).ToString(), _ => Interlocked.Increment(ref _nextFeeId));
                var npi = r.Span(c.Npi);
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(npi.Length == 12 && npi.StartsWith("10") ? npi[2..].ToString() : npi.ToString()); // file has "10" + NPI
                w.Write(network, NpgsqlDbType.Integer);
                w.Write((short)(Flags(r, c) & ~NetworkFlags.InArea), NpgsqlDbType.Smallint); // shared across regions
                w.Write(feeId, NpgsqlDbType.Integer);
                w.WriteText(r.Text(c.Zip));
                n++;
            }
        }
        await w.CompleteAsync(ct);
        return n;
    }

    private async Task WriteFeeSchedules(int releaseId, ConcurrentDictionary<string, int> fees)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var w = await conn.BeginBinaryImportAsync("""
            copy cms.spuf_fee_schedule (release_id, fee_schedule_id, floor_price, brand_fee_30, brand_fee_60, brand_fee_90,
                generic_fee_30, generic_fee_60, generic_fee_90, selected_fee_30, selected_fee_60, selected_fee_90)
            from stdin (format binary)
            """);
        foreach (var (key, id) in fees)
        {
            w.StartRow();
            w.Write(releaseId, NpgsqlDbType.Integer);
            w.Write(id, NpgsqlDbType.Integer);
            foreach (var v in ParseFees(key)) w.WriteNumeric(v);
        }
        await w.CompleteAsync();
    }

    private async Task WritePlanNetworks(int releaseId, IReadOnlyDictionary<string, int> networkOf)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var w = await conn.BeginBinaryImportAsync(
            "copy cms.spuf_plan_network (release_id, contract_id, plan_id, segment_id, network_id) from stdin (format binary)");
        foreach (var (plan, network) in networkOf)
        {
            var k = plan.Split('|');
            w.StartRow();
            w.Write(releaseId, NpgsqlDbType.Integer);
            w.WriteText(k[0]); w.WriteText(k[1]); w.WriteText(k[2]);
            w.Write(network, NpgsqlDbType.Integer);
        }
        await w.CompleteAsync();
    }

    private async Task<long> WriteSummaries(int releaseId, Dictionary<string, PlanAgg> plans)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var w = await conn.BeginBinaryImportAsync("""
            copy cms.spuf_network_summary (release_id, contract_id, plan_id, segment_id, pharmacy_type, pharmacy_count, in_area_count,
                brand_fee_30, brand_fee_60, brand_fee_90, generic_fee_30, generic_fee_60, generic_fee_90,
                selected_fee_30, selected_fee_60, selected_fee_90, floor_price)
            from stdin (format binary)
            """);
        long n = 0;
        foreach (var (key, plan) in plans)
        {
            var k = key.Split('|');
            for (var t = 0; t < plan.Types.Length; t++)
            {
                var agg = plan.Types[t];
                if (agg.Count == 0) continue;
                var f = ParseFees(agg.TypicalFees());

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

    /// <summary>Floor price then the nine fees; the floor is null when the release has no FLOOR_PRICE column.</summary>
    private static decimal?[] ParseFees(string raw)
    {
        var f = raw.Split('|').Select(Parse).ToArray();
        return f.Length == FeeColumns.Length ? [null, .. f] : f;
    }

    private static decimal? Parse(string value)
    {
        var s = value.Trim();
        return s is "" or "." ? null : decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>One plan: summaries per pharmacy type and an order-independent fingerprint of all its rows.</summary>
    private sealed class PlanAgg
    {
        public readonly TypeAgg[] Types = [new(), new(), new(), new()];
        public UInt128 Sum;
        public UInt128 Xor;
        public long Rows;

        public void AddToFingerprint(ReadOnlySpan<char> pharmacy, ReadOnlySpan<char> fees)
        {
            var first = XxHash128.HashToUInt128(MemoryMarshal.AsBytes(pharmacy));
            var h = XxHash128.HashToUInt128(MemoryMarshal.AsBytes(fees), seed: (long)(ulong)first);
            Sum += h;
            Xor ^= h;
            Rows++;
        }

        public void Merge(PlanAgg other)
        {
            Sum += other.Sum;
            Xor ^= other.Xor;
            Rows += other.Rows;
            for (var i = 0; i < Types.Length; i++) Types[i].Merge(other.Types[i]);
        }
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
