using System.Diagnostics;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace PlanD.Data.Ingest;

/// <summary>
/// Loads the stand-alone PDPs of an extracted SPUF release (monthly or quarterly) into the cms.spuf_* tables.
/// Medicare Advantage plans (H and R contracts) are skipped: plan-d quotes PDPs only.
/// Layouts and quirks: docs/research/part-d-puf.md.
/// </summary>
public sealed partial class SpufLoader(NpgsqlDataSource db, Action<string> log)
{
    /// <summary>Formularies used by the PDPs loaded from the plan file; other formularies are skipped.</summary>
    private HashSet<string> _formularies = [];

    /// <summary>Stand-alone PDP contract ids start with S (H = local MA, R = regional MA).</summary>
    public static bool IsPdp(ReadOnlySpan<char> contractId) => contractId.StartsWith("S");

    public async Task<int> LoadAsync(string directory, int planYear, string label, CancellationToken ct = default)
    {
        var files = SpufFiles.Find(directory);
        var releaseId = await Releases.CreateAsync(db, ReleaseSource.Spuf, planYear, label, directory);
        log($"Release {releaseId}: {label} (plan year {planYear}) from {directory}");
        var counts = new Dictionary<string, long>();

        try
        {
            counts["spuf_geo"] = await Timed("geographic locator", () => LoadGeo(releaseId, files.Geo));
            counts["spuf_plan"] = await Timed("plan information", () => LoadPlans(releaseId, files.Plans));
            counts["spuf_formulary"] = await Timed("formulary", () => LoadFormulary(releaseId, files.Formulary));
            counts["spuf_excluded_drug"] = await Timed("excluded drugs", () => LoadExcluded(releaseId, files.Excluded));
            counts["spuf_indication"] = await Timed("indication-based coverage", () => LoadIndications(releaseId, files.Indications));
            counts["spuf_beneficiary_cost"] = await Timed("beneficiary cost", () => LoadBeneficiaryCost(releaseId, files.BeneficiaryCost));
            counts["spuf_insulin_cost"] = await Timed("insulin cost", () => LoadInsulin(releaseId, files.Insulin));

            // The two big files are independent: stream the pharmacy network summary while pricing copies.
            var pricing = files.Pricing is null
                ? Task.FromResult(0L)
                : Timed("pricing", () => LoadPricing(releaseId, files.Pricing));
            var networks = Timed("pharmacy networks", () => new NetworkSummarizer(db, log).LoadAsync(releaseId, files.Pharmacy, ct));
            counts["spuf_pricing"] = await pricing;
            var network = await networks;
            counts["spuf_network_summary"] = network.Summaries;
            counts["spuf_plan_network"] = network.Networks;
            counts["spuf_network_pharmacy"] = network.NetworkRows;
            counts["spuf_fee_schedule"] = network.FeeSchedules;

            if (files.Pricing is not null)
                counts["spuf_drug_price"] = await Timed("drug price averages", () => BuildDrugPrices(releaseId));

            await Timed("analyze", () => Analyze());
            await Check(releaseId, counts, hasPricing: files.Pricing is not null);
            await Releases.MarkReadyAsync(db, releaseId, counts);
            log($"Release {releaseId} ready: {string.Join(", ", counts.Select(c => $"{c.Key}={c.Value:N0}"))}");
            return releaseId;
        }
        catch
        {
            await Releases.MarkFailedAsync(db, releaseId);
            throw;
        }
    }

    private async Task<long> LoadGeo(int releaseId, string path)
    {
        using var r = new DelimitedReader(path, '|');
        int code = r.Column("COUNTY_CODE"), state = r.Column("STATENAME"), county = r.Column("COUNTY"),
            maCode = r.Column("MA_REGION_CODE"), ma = r.Column("MA_REGION"),
            pdpCode = r.Column("PDP_REGION_CODE"), pdp = r.Column("PDP_REGION");

        return await Copy("cms.spuf_geo (release_id, county_code, state_name, county_name, ma_region_code, ma_region, pdp_region_code, pdp_region)", w =>
        {
            long n = 0;
            while (r.Read())
            {
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(r.Required(code));
                w.WriteText(r.Required(state));
                w.WriteText(r.Required(county));
                w.WriteText(r.Text(maCode));
                w.WriteText(r.Text(ma));
                w.WriteText(r.Text(pdpCode));
                w.WriteText(r.Text(pdp));
                n++;
            }
            return n;
        });
    }

    private async Task<long> LoadPlans(int releaseId, string path)
    {
        using var r = new DelimitedReader(path, '|', Encodings.Windows1252);
        int contract = r.Column("CONTRACT_ID"), plan = r.Column("PLAN_ID"), segment = r.Column("SEGMENT_ID"),
            contractName = r.Column("CONTRACT_NAME"), planName = r.Column("PLAN_NAME"), formulary = r.Column("FORMULARY_ID"),
            premium = r.Column("PREMIUM"), deductible = r.Column("DEDUCTIBLE"), pdpRegion = r.Column("PDP_REGION_CODE"),
            suppressed = r.Column("PLAN_SUPPRESSED_YN");

        var plans = new Dictionary<(string, string, string), (string ContractName, string PlanName, string Formulary, decimal? Premium, decimal Deductible, string? Region)>();
        while (r.Read())
        {
            if (!IsPdp(r.Span(contract)) || r.Flag(suppressed)) continue;
            plans.TryAdd((r.Required(contract), r.Required(plan), r.Required(segment)), (
                r.Required(contractName), r.Required(planName), r.Required(formulary), r.Decimal(premium),
                r.Decimal(deductible) ?? 0m, r.Text(pdpRegion)));
        }
        _formularies = plans.Values.Select(v => v.Formulary).ToHashSet();

        return await Copy("cms.spuf_plan (release_id, contract_id, plan_id, segment_id, contract_name, plan_name, formulary_id, premium, deductible, pdp_region_code)", w =>
        {
            foreach (var ((c, p, s), v) in plans)
            {
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(c); w.WriteText(p); w.WriteText(s);
                w.WriteText(v.ContractName); w.WriteText(v.PlanName); w.WriteText(v.Formulary);
                w.WriteNumeric(v.Premium); w.WriteNumeric(v.Deductible);
                w.WriteText(v.Region);
            }
            return plans.Count;
        });
    }

    private async Task<long> LoadFormulary(int releaseId, string path)
    {
        using var r = new DelimitedReader(path, '|');
        int id = r.Column("FORMULARY_ID"), version = r.Column("FORMULARY_VERSION"), rxcui = r.Column("RXCUI"), ndc = r.Column("NDC"),
            tier = r.Column("TIER_LEVEL_VALUE"), ql = r.Column("QUANTITY_LIMIT_YN"), qlAmount = r.Column("QUANTITY_LIMIT_AMOUNT"),
            qlDays = r.Column("QUANTITY_LIMIT_DAYS"), pa = r.Column("PRIOR_AUTHORIZATION_YN"), st = r.Column("STEP_THERAPY_YN"),
            selected = r.Column("SELECTED_DRUG_YN");

        return await Copy("cms.spuf_formulary (release_id, formulary_id, formulary_version, rxcui, ndc, tier, quantity_limit, ql_amount, ql_days, prior_auth, step_therapy, selected_drug)", w =>
        {
            long n = 0;
            while (r.Read())
            {
                if (!_formularies.Contains(r.Required(id))) continue;
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(r.Required(id));
                w.Write(r.Int(version) ?? 0, NpgsqlDbType.Integer);
                w.WriteText(r.Required(rxcui));
                w.WriteText(r.Required(ndc));
                w.Write((short)r.Int(tier)!.Value, NpgsqlDbType.Smallint);
                w.Write(r.Flag(ql), NpgsqlDbType.Boolean);
                w.WriteNumeric(r.Decimal(qlAmount));
                w.WriteInt(r.Int(qlDays));
                w.Write(r.Flag(pa), NpgsqlDbType.Boolean);
                w.Write(r.Flag(st), NpgsqlDbType.Boolean);
                w.Write(r.Flag(selected), NpgsqlDbType.Boolean);
                n++;
            }
            return n;
        });
    }

    private async Task<long> LoadExcluded(int releaseId, string path)
    {
        using var r = new DelimitedReader(path, '|');
        int contract = r.Column("CONTRACT_ID"), plan = r.Column("PLAN_ID"), rxcui = r.Column("RXCUI"), tier = r.Column("TIER"),
            ql = r.Column("QUANTITY_LIMIT_YN"), qlAmount = r.Column("QUANTITY_LIMIT_AMOUNT"), qlDays = r.Column("QUANTITY_LIMIT_DAYS"),
            pa = r.Column("PRIOR_AUTH_YN"), st = r.Column("STEP_THERAPY_YN"), capped = r.Column("CAPPED_BENEFIT_YN");

        return await Copy("cms.spuf_excluded_drug (release_id, contract_id, plan_id, rxcui, tier, quantity_limit, ql_amount, ql_days, prior_auth, step_therapy, capped_benefit)", w =>
        {
            long n = 0;
            while (r.Read())
            {
                if (!IsPdp(r.Span(contract))) continue;
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(r.Required(contract));
                w.WriteText(r.Required(plan));
                w.WriteText(r.Required(rxcui));
                w.Write((short)r.Int(tier)!.Value, NpgsqlDbType.Smallint);
                w.Write(r.Flag(ql), NpgsqlDbType.Boolean); // 0/1 in this file
                w.WriteNumeric(r.Decimal(qlAmount));
                w.WriteInt(r.Int(qlDays));
                w.Write(r.Flag(pa), NpgsqlDbType.Boolean);
                w.Write(r.Flag(st), NpgsqlDbType.Boolean);
                w.Write(r.Flag(capped), NpgsqlDbType.Boolean);
                n++;
            }
            return n;
        });
    }

    private async Task<long> LoadIndications(int releaseId, string path)
    {
        using var r = new DelimitedReader(path, '|');
        int contract = r.Column("CONTRACT_ID"), plan = r.Column("PLAN_ID"), rxcui = r.Column("RXCUI"), disease = r.Column("DISEASE");

        return await Copy("cms.spuf_indication (release_id, contract_id, plan_id, rxcui, disease)", w =>
        {
            long n = 0;
            while (r.Read())
            {
                if (!IsPdp(r.Span(contract))) continue;
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(r.Required(contract));
                w.WriteText(r.Required(plan));
                w.WriteText(r.Required(rxcui));
                w.WriteText(r.Required(disease));
                n++;
            }
            return n;
        });
    }

    private static readonly string[] CostGroups = ["PREF", "NONPREF", "MAIL_PREF", "MAIL_NONPREF"];

    private async Task<long> LoadBeneficiaryCost(int releaseId, string path)
    {
        using var r = new DelimitedReader(path, '|');
        int contract = r.Column("CONTRACT_ID"), plan = r.Column("PLAN_ID"), segment = r.Column("SEGMENT_ID"),
            level = r.Column("COVERAGE_LEVEL"), tier = r.Column("TIER"), days = r.Column("DAYS_SUPPLY"),
            specialty = r.Column("TIER_SPECIALTY_YN"), ded = r.Column("DED_APPLIES_YN");
        var groups = CostGroups.Select(g => (
            Type: r.Column($"COST_TYPE_{g}"), Amt: r.Column($"COST_AMT_{g}"),
            Min: r.Column($"COST_MIN_AMT_{g}"), Max: r.Column($"COST_MAX_AMT_{g}"))).ToArray();

        return await Copy("""
            cms.spuf_beneficiary_cost (release_id, contract_id, plan_id, segment_id, coverage_level, tier, days_supply,
                pref_type, pref_amt, pref_min, pref_max, std_type, std_amt, std_min, std_max,
                mail_pref_type, mail_pref_amt, mail_pref_min, mail_pref_max, mail_std_type, mail_std_amt, mail_std_min, mail_std_max,
                tier_specialty, ded_applies)
            """, w =>
        {
            long n = 0;
            while (r.Read())
            {
                if (!IsPdp(r.Span(contract))) continue;
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(r.Required(contract));
                w.WriteText(r.Required(plan));
                w.WriteText(r.Required(segment));
                w.Write((short)r.Int(level)!.Value, NpgsqlDbType.Smallint);
                w.Write((short)r.Int(tier)!.Value, NpgsqlDbType.Smallint);
                w.Write(DaysFromCode(r.Int(days)!.Value), NpgsqlDbType.Smallint);
                foreach (var g in groups)
                {
                    w.Write((short)(r.Int(g.Type) ?? 0), NpgsqlDbType.Smallint);
                    w.WriteNumeric(r.Decimal(g.Amt));
                    w.WriteNumeric(r.Decimal(g.Min));
                    w.WriteNumeric(r.Decimal(g.Max));
                }
                w.Write(r.Flag(specialty), NpgsqlDbType.Boolean);
                w.Write(r.Flag(ded), NpgsqlDbType.Boolean);
                n++;
            }
            return n;
        });
    }

    private async Task<long> LoadInsulin(int releaseId, string path)
    {
        using var r = new DelimitedReader(path, '|');
        int contract = r.Column("CONTRACT_ID"), plan = r.Column("PLAN_ID"), segment = r.Column("SEGMENT_ID"),
            tier = r.Column("TIER"), days = r.Column("DAYS_SUPPLY");
        var copays = new[] { "copay_amt_pref_insln", "copay_amt_nonpref_insln", "copay_amt_mail_pref_insln", "copay_amt_mail_nonpref_insln" }
            .Select(c => r.Column(c)).ToArray();
        var coins = new[] { "coin_amt_pref_insln", "coin_amt_nonpref_insln", "coin_amt_mail_pref_insln", "coin_amt_mail_nonpref_insln" }
            .Select(c => r.Column(c)).ToArray();

        return await Copy("""
            cms.spuf_insulin_cost (release_id, contract_id, plan_id, segment_id, tier, days_supply,
                copay_pref, copay_std, copay_mail_pref, copay_mail_std, coins_pref, coins_std, coins_mail_pref, coins_mail_std)
            """, w =>
        {
            long n = 0;
            while (r.Read())
            {
                if (!IsPdp(r.Span(contract))) continue;
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(r.Required(contract));
                w.WriteText(r.Required(plan));
                w.WriteText(r.Required(segment));
                w.WriteSmallint(r.Int(tier)); // "." = every tier
                w.Write(DaysFromCode(r.Int(days)!.Value), NpgsqlDbType.Smallint);
                foreach (var c in copays) w.WriteNumeric(r.Decimal(c));
                foreach (var c in coins) w.WriteNumeric(r.Decimal(c));
                n++;
            }
            return n;
        });
    }

    private async Task<long> LoadPricing(int releaseId, string path)
    {
        using var r = new DelimitedReader(path, '|');
        int contract = r.Column("CONTRACT_ID"), plan = r.Column("PLAN_ID"), segment = r.Column("SEGMENT_ID"),
            ndc = r.Column("NDC"), days = r.Column("DAYS_SUPPLY"), cost = r.Column("UNIT_COST");

        // The file is sorted by plan, NDC and days supply: fold each plan-NDC run into one row.
        return await Copy("cms.spuf_pricing (release_id, contract_id, plan_id, segment_id, ndc, unit_cost_30, unit_cost_60, unit_cost_90)", w =>
        {
            long rows = 0, written = 0;
            string? key = null;
            string[] current = [];
            var costs = new decimal?[3];

            void Flush()
            {
                if (key is null) return;
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                foreach (var part in current) w.WriteText(part);
                foreach (var c in costs) w.WriteNumeric(c);
                written++;
            }

            while (r.Read())
            {
                if (!IsPdp(r.Span(contract))) continue;
                var rowKey = r.Range(contract, segment).ToString() + "|" + r.Span(ndc).ToString();
                if (rowKey != key)
                {
                    Flush();
                    key = rowKey;
                    current = [r.Required(contract), r.Required(plan), r.Required(segment), r.Required(ndc)];
                    Array.Clear(costs);
                }
                var slot = r.Int(days) switch
                {
                    30 => 0,
                    60 => 1,
                    90 => 2,
                    var d => throw new InvalidDataException($"Pricing row {r.Row}: unexpected days supply {d}."),
                };
                if (costs[slot] is not null)
                    throw new InvalidDataException($"Pricing row {r.Row}: {rowKey} is not sorted or repeats {r.Int(days)} days.");
                costs[slot] = r.Decimal(cost);
                if (++rows % 10_000_000 == 0) log($"  pricing: {rows:N0} rows");
            }
            Flush();
            log($"  pricing: {rows:N0} rows folded into {written:N0} plan-drug rows");
            return written;
        });
    }

    private async Task<long> BuildDrugPrices(int releaseId)
    {
        await using var conn = await db.OpenConnectionAsync();
        // Average rather than median: a hash aggregate needs no 50M-row sort spilling to disk.
        return await conn.ExecuteAsync("""
            insert into cms.spuf_drug_price (release_id, rxcui, ndc, days_supply, unit_cost)
            select @releaseId, f.rxcui, x.ndc, x.days, avg(x.cost)::numeric(14, 4)
            from (
                select p.ndc, d.days, d.cost
                from cms.spuf_pricing p
                cross join lateral (values (30::smallint, p.unit_cost_30), (60::smallint, p.unit_cost_60), (90::smallint, p.unit_cost_90)) d(days, cost)
                where p.release_id = @releaseId and d.cost > 0
            ) x
            join (select distinct rxcui, ndc from cms.spuf_formulary where release_id = @releaseId) f on f.ndc = x.ndc
            group by f.rxcui, x.ndc, x.days
            """, new { releaseId });
    }

    private async Task Analyze()
    {
        await using var conn = await db.OpenConnectionAsync();
        foreach (var table in new[] { "spuf_plan", "spuf_geo", "spuf_formulary", "spuf_excluded_drug",
                     "spuf_beneficiary_cost", "spuf_insulin_cost", "spuf_pricing", "spuf_drug_price", "spuf_network_summary",
                     "spuf_plan_network", "spuf_network_pharmacy", "spuf_fee_schedule" })
            await conn.ExecuteAsync($"analyze cms.{table}");
    }

    /// <summary>Structural checks before a release can be activated.</summary>
    private async Task Check(int releaseId, Dictionary<string, long> counts, bool hasPricing)
    {
        foreach (var (table, n) in counts)
            if (n == 0 && table is not "spuf_indication")
                throw new InvalidDataException($"{table} loaded no rows.");

        await using var conn = await db.OpenConnectionAsync();
        var plansWithoutCosts = await conn.ExecuteScalarAsync<long>("""
            select count(*) from cms.spuf_plan p
            where p.release_id = @releaseId and not exists (
                select 1 from cms.spuf_beneficiary_cost c
                where c.release_id = p.release_id and c.contract_id = p.contract_id and c.plan_id = p.plan_id and c.segment_id = p.segment_id)
            """, new { releaseId });
        if (plansWithoutCosts > 0)
            throw new InvalidDataException($"{plansWithoutCosts} plans have no beneficiary cost rows.");

        var formulariesMissing = await conn.ExecuteScalarAsync<long>("""
            select count(distinct p.formulary_id) from cms.spuf_plan p
            where p.release_id = @releaseId and not exists (
                select 1 from cms.spuf_formulary f where f.release_id = p.release_id and f.formulary_id = p.formulary_id)
            """, new { releaseId });
        if (formulariesMissing > 0)
            log($"  warning: {formulariesMissing} formulary ids referenced by plans have no formulary rows");

        if (hasPricing && counts["spuf_pricing"] < 100_000)
            throw new InvalidDataException($"Pricing has only {counts["spuf_pricing"]:N0} rows.");
    }

    private static short DaysFromCode(int code) => code switch
    {
        1 => 30,
        2 => 90,
        4 => 60,
        _ => throw new InvalidDataException($"Unexpected DAYS_SUPPLY code {code} (expected 1, 2 or 4)."),
    };

    private async Task<long> Copy(string target, Func<NpgsqlBinaryImporter, long> writeRows)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var writer = await conn.BeginBinaryImportAsync($"copy {target} from stdin (format binary)");
        var n = await Task.Run(() => writeRows(writer));
        await writer.CompleteAsync();
        return n;
    }

    private async Task<T> Timed<T>(string step, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        log($"Loading {step}...");
        var result = await action();
        log($"  {step} done in {sw.Elapsed:mm\\:ss}");
        return result;
    }

    private async Task Timed(string step, Func<Task> action) => await Timed(step, async () => { await action(); return 0; });
}

/// <summary>Finds the SPUF tables in an extracted release. Names vary in spacing, case and suffix between releases.</summary>
public sealed partial record SpufFiles(
    string Plans, string Geo, string Formulary, string Excluded, string Indications,
    string BeneficiaryCost, string Insulin, string? Pricing, IReadOnlyList<string> Pharmacy)
{
    public static SpufFiles Find(string directory)
    {
        var txt = Directory.GetFiles(directory, "*.txt")
            .Where(f => !Path.GetFileName(f).Contains("sample", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        string One(Regex pattern, string what) => txt.SingleOrDefault(f => pattern.IsMatch(Path.GetFileName(f)))
            ?? throw new FileNotFoundException($"No {what} file in {directory}.");

        var pharmacy = txt.Where(f => PharmacyPattern().IsMatch(Path.GetFileName(f))).Order().ToArray();
        if (pharmacy.Length == 0) throw new FileNotFoundException($"No pharmacy network files in {directory}.");

        return new SpufFiles(
            Plans: One(PlanPattern(), "plan information"),
            Geo: One(GeoPattern(), "geographic locator"),
            Formulary: One(FormularyPattern(), "basic drugs formulary"),
            Excluded: One(ExcludedPattern(), "excluded drugs formulary"),
            Indications: One(IndicationPattern(), "indication based coverage"),
            BeneficiaryCost: One(CostPattern(), "beneficiary cost"),
            Insulin: One(InsulinPattern(), "insulin beneficiary cost"),
            Pricing: txt.SingleOrDefault(f => PricingPattern().IsMatch(Path.GetFileName(f))),
            Pharmacy: pharmacy);
    }

    [GeneratedRegex(@"^plan\s+information", RegexOptions.IgnoreCase)] private static partial Regex PlanPattern();
    [GeneratedRegex(@"^geographic\s+locator", RegexOptions.IgnoreCase)] private static partial Regex GeoPattern();
    [GeneratedRegex(@"^basic\s+drugs\s+formulary", RegexOptions.IgnoreCase)] private static partial Regex FormularyPattern();
    [GeneratedRegex(@"^excluded\s+drugs\s+formulary", RegexOptions.IgnoreCase)] private static partial Regex ExcludedPattern();
    [GeneratedRegex(@"^indication\s+based\s+coverage", RegexOptions.IgnoreCase)] private static partial Regex IndicationPattern();
    [GeneratedRegex(@"^beneficiary\s+cost", RegexOptions.IgnoreCase)] private static partial Regex CostPattern();
    [GeneratedRegex(@"^insulin\s+beneficiary\s+cost", RegexOptions.IgnoreCase)] private static partial Regex InsulinPattern();
    [GeneratedRegex(@"^pricing\s+file", RegexOptions.IgnoreCase)] private static partial Regex PricingPattern();
    [GeneratedRegex(@"^pharmacy\s+networks?\s+file", RegexOptions.IgnoreCase)] private static partial Regex PharmacyPattern();
}

internal static class BinaryImporterExtensions
{
    public static void WriteText(this NpgsqlBinaryImporter w, string? value)
    {
        if (value is null) w.WriteNull();
        else w.Write(value, NpgsqlDbType.Text);
    }

    public static void WriteNumeric(this NpgsqlBinaryImporter w, decimal? value)
    {
        if (value is null) w.WriteNull();
        else w.Write(value.Value, NpgsqlDbType.Numeric);
    }

    public static void WriteInt(this NpgsqlBinaryImporter w, int? value)
    {
        if (value is null) w.WriteNull();
        else w.Write(value.Value, NpgsqlDbType.Integer);
    }

    public static void WriteSmallint(this NpgsqlBinaryImporter w, int? value)
    {
        if (value is null) w.WriteNull();
        else w.Write((short)value.Value, NpgsqlDbType.Smallint);
    }
}
