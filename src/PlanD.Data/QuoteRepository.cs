using Dapper;
using Npgsql;
using PlanD.Engine;
using PlanD.Engine.Quoting;

namespace PlanD.Data;

public sealed record CountyMatch(string CountyCode, string CountyName, string State, decimal Share);

public sealed record DrugSearchResult(string Rxcui, string Name, string TermType, bool OnAnyFormulary);

/// <summary>Reads the active releases to answer quote, ZIP and drug-search queries.</summary>
public sealed class QuoteRepository(NpgsqlDataSource db) : IQuoteData
{
    /// <summary>
    /// ZIP → ZCTA (PO-box ZIPs map to a nearby ZCTA) → county FIPS → SSA code, with each county's share
    /// of the ZIP's population. A ZIP with no crosswalk row is tried as a ZCTA directly.
    /// </summary>
    public async Task<IReadOnlyList<CountyMatch>> ResolveZipAsync(string zip, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var geo = await Releases.ActiveAsync(conn, ReleaseSource.Geo, null) ?? throw NoRelease("geo");
        return (await conn.QueryAsync<CountyMatch>("""
            with z as (
                select coalesce((select zcta from cms.zip_zcta where release_id = @geo and zip = @zip), @zip) as zcta
            )
            select s.ssa_code as CountyCode, zc.county_name as CountyName, s.state as State, zc.share as Share
            from z
            join cms.zcta_county zc on zc.release_id = @geo and zc.zcta = z.zcta
            join cms.fips_ssa s on s.release_id = @geo and s.county_fips = zc.county_fips
            order by zc.share desc, s.ssa_code
            """, new { geo, zip = zip.Trim().PadLeft(5, '0') })).ToList();
    }

    public async Task<IReadOnlyList<DrugSearchResult>> SearchDrugsAsync(string text, int year, int limit = 25, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rx = await Releases.ActiveAsync(conn, ReleaseSource.RxNorm, null) ?? throw NoRelease("rxnorm");
        var spuf = await Releases.ActiveAsync(conn, ReleaseSource.Spuf, year);
        var terms = text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var where = string.Join(" and ", terms.Select((_, i) => $"lower(c.name) like @t{i}"));
        var args = new DynamicParameters(new { rx, spuf, limit });
        for (var i = 0; i < terms.Length; i++) args.Add($"t{i}", $"%{terms[i]}%");

        return (await conn.QueryAsync<DrugSearchResult>($"""
            select c.rxcui as Rxcui, c.name as Name, c.tty as TermType,
                   exists (select 1 from cms.spuf_formulary f where f.release_id = @spuf and f.rxcui = c.rxcui) as OnAnyFormulary
            from cms.rx_concept c
            where c.release_id = @rx and c.tty in ('SCD', 'SBD', 'GPCK', 'BPCK') and {where}
            order by OnAnyFormulary desc, length(c.name), c.name
            limit @limit
            """, args)).ToList();
    }

    public async Task<IReadOnlyList<PlanOffer>> PlansForCountyAsync(int year, string countyCode, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var spuf = await Releases.ActiveAsync(conn, ReleaseSource.Spuf, year) ?? throw NoRelease($"spuf {year}");
        var land = await Releases.ActiveAsync(conn, ReleaseSource.Landscape, year);

        // PDPs are sold by PDP region; employer-only plans (plan id 800+) aren't open to individuals.
        var rows = await conn.QueryAsync("""
            select p.contract_id, p.plan_id, p.segment_id, p.plan_name, p.formulary_id, p.deductible, p.premium as spuf_premium,
                   l.organization_name, l.drug_benefit_type, l.part_d_total_premium, l.part_d_lis_premium, l.star_part_d, l.lis_benchmark
            from cms.spuf_plan p
            join cms.spuf_geo g on g.release_id = p.release_id and g.county_code = @countyCode and g.pdp_region_code = p.pdp_region_code
            left join cms.landscape_plan l
                   on l.release_id = @land and l.contract_id = p.contract_id and l.plan_id = p.plan_id and l.segment_id = p.segment_id
            where p.release_id = @spuf and p.plan_id < '800'
            """, new { spuf, land, countyCode });

        return rows.Select(r => new PlanOffer(
            new PlanKey((string)r.contract_id, (string)r.plan_id, (string)r.segment_id),
            (string)r.plan_name,
            (string?)r.organization_name,
            ParseBenefitType((string?)r.drug_benefit_type),
            (string)r.formulary_id,
            (decimal)r.deductible,
            (decimal?)r.part_d_total_premium ?? (decimal?)r.spuf_premium,
            (decimal?)r.part_d_lis_premium,
            (string?)r.star_part_d,
            (bool?)r.lis_benchmark)).ToList();
    }

    public async Task<IReadOnlyList<DrugInfo>> DrugInfoAsync(IReadOnlyCollection<string> rxcuis, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rx = await Releases.ActiveAsync(conn, ReleaseSource.RxNorm, null) ?? throw NoRelease("rxnorm");
        var found = (await conn.QueryAsync<DrugInfo>(
            "select rxcui as Rxcui, name as Name, tty as TermType from cms.rx_concept where release_id = @rx and rxcui = any(@rxcuis)",
            new { rx, rxcuis = rxcuis.ToArray() })).ToDictionary(d => d.Rxcui);
        return rxcuis.Select(id => found.GetValueOrDefault(id) ?? new DrugInfo(id, $"RXCUI {id}", null)).ToList();
    }

    public async Task<PlanDrugData> PlanDrugDataAsync(int year, IReadOnlyList<PlanOffer> plans, IReadOnlyCollection<string> rxcuis, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var spuf = await Releases.ActiveAsync(conn, ReleaseSource.Spuf, year) ?? throw NoRelease($"spuf {year}");
        var keys = new
        {
            spuf,
            c = plans.Select(p => p.Key.ContractId).ToArray(),
            p = plans.Select(p => p.Key.PlanId).ToArray(),
            s = plans.Select(p => p.Key.SegmentId).ToArray(),
            rxcuis = rxcuis.ToArray(),
            fids = plans.Select(p => p.FormularyId).Distinct().ToArray(),
        };
        const string planKeys = "unnest(@c::text[], @p::text[], @s::text[]) as k(contract_id, plan_id, segment_id)";

        var costRows = await conn.QueryAsync($"""
            select b.* from {planKeys}
            join cms.spuf_beneficiary_cost b on b.release_id = @spuf
                 and b.contract_id = k.contract_id and b.plan_id = k.plan_id and b.segment_id = k.segment_id
            """, keys);
        var insulinRows = await conn.QueryAsync($"""
            select i.* from {planKeys}
            join cms.spuf_insulin_cost i on i.release_id = @spuf
                 and i.contract_id = k.contract_id and i.plan_id = k.plan_id and i.segment_id = k.segment_id
            """, keys);
        var formulary = (await conn.QueryAsync(
            "select * from cms.spuf_formulary where release_id = @spuf and formulary_id = any(@fids) and rxcui = any(@rxcuis)", keys)).ToList();
        var excluded = await conn.QueryAsync($"""
            select distinct e.* from {planKeys}
            join cms.spuf_excluded_drug e on e.release_id = @spuf and e.contract_id = k.contract_id and e.plan_id = k.plan_id
            where e.rxcui = any(@rxcuis)
            """, keys);
        var ndcs = formulary.Select(f => (string)f.ndc).Distinct().ToArray();
        var pricing = await conn.QueryAsync($"""
            select pr.contract_id, pr.plan_id, pr.segment_id, pr.ndc, pr.unit_cost_30, pr.unit_cost_60, pr.unit_cost_90 from {planKeys}
            join cms.spuf_pricing pr on pr.release_id = @spuf
                 and pr.contract_id = k.contract_id and pr.plan_id = k.plan_id and pr.segment_id = k.segment_id
            where pr.ndc = any(@ndcs)
            """, new { keys.spuf, keys.c, keys.p, keys.s, ndcs });
        var market = await conn.QueryAsync(
            "select rxcui, days_supply, unit_cost from cms.spuf_drug_price where release_id = @spuf and rxcui = any(@rxcuis)", keys);
        var networks = await conn.QueryAsync($"""
            select n.* from {planKeys}
            join cms.spuf_network_summary n on n.release_id = @spuf
                 and n.contract_id = k.contract_id and n.plan_id = k.plan_id and n.segment_id = k.segment_id
            """, keys);

        // --- assemble -----------------------------------------------------------------------------
        var benefits = new Dictionary<PlanKey, PlanBenefit>();
        var tiersByPlan = costRows.GroupBy(r => Key((object)r)).ToDictionary(g => g.Key, g => g.Select(ToTierCost).ToList());
        var insulinByPlan = insulinRows.GroupBy(r => Key((object)r)).ToDictionary(g => g.Key, g => g.Select(ToInsulinCost).ToList());
        foreach (var plan in plans)
        {
            if (!tiersByPlan.TryGetValue(plan.Key, out var tiers)) continue;
            benefits[plan.Key] = new PlanBenefit(
                plan.Key, plan.Deductible,
                // Without a landscape row, don't assume the enhanced-plan TrOOP credit.
                plan.BenefitType ?? DrugBenefitType.ActuariallyEquivalent,
                tiers, insulinByPlan.GetValueOrDefault(plan.Key));
        }

        var unitCosts = pricing.ToDictionary(
            r => (Key((object)r), (string)r.ndc),
            r => ByDays((decimal?)r.unit_cost_30, (decimal?)r.unit_cost_60, (decimal?)r.unit_cost_90));
        var marketCosts = market
            .GroupBy(r => (string)r.rxcui)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<int, decimal>)g.ToDictionary(r => (int)(short)r.days_supply, r => (decimal)r.unit_cost));
        var formularyRows = formulary.ToLookup(f => ((string)f.formulary_id, (string)f.rxcui));
        var excludedRows = excluded.ToLookup(e => ((string)e.contract_id, (string)e.plan_id, (string)e.rxcui));

        var drugs = new Dictionary<(PlanKey, string), PlanDrug>();
        foreach (var plan in plans)
        foreach (var rxcui in rxcuis)
        {
            if (formularyRows[(plan.FormularyId, rxcui)].FirstOrDefault() is { } f)
            {
                drugs[(plan.Key, rxcui)] = new PlanDrug(
                    rxcui, CoverageStatus.Covered, (short)f.tier, f.prior_auth, f.step_therapy, f.quantity_limit,
                    (decimal?)f.ql_amount, (int?)f.ql_days, f.selected_drug,
                    unitCosts.GetValueOrDefault((plan.Key, (string)f.ndc)) ?? marketCosts.GetValueOrDefault(rxcui),
                    UnitCostIsFallback: !unitCosts.ContainsKey((plan.Key, (string)f.ndc)));
            }
            else if (excludedRows[(plan.Key.ContractId, plan.Key.PlanId, rxcui)].FirstOrDefault() is { } e)
            {
                // Supplemental coverage of a Part D-excluded drug; the pricing file has no unit cost for these.
                drugs[(plan.Key, rxcui)] = new PlanDrug(
                    rxcui, CoverageStatus.CoveredSupplemental, (short)e.tier, e.prior_auth, e.step_therapy, e.quantity_limit,
                    (decimal?)e.ql_amount, (int?)e.ql_days, false, marketCosts.GetValueOrDefault(rxcui), UnitCostIsFallback: true);
            }
        }

        var networkInfo = networks.ToDictionary(
            n => (Key((object)n), PharmacyTypeFrom((string)n.pharmacy_type)),
            n => (NetworkInfo)new NetworkInfo((int)n.pharmacy_count, (int)n.in_area_count, new DispensingFees(
                [Fee(n.brand_fee_30), Fee(n.brand_fee_60), Fee(n.brand_fee_90)],
                [Fee(n.generic_fee_30), Fee(n.generic_fee_60), Fee(n.generic_fee_90)],
                [(decimal?)n.selected_fee_30, (decimal?)n.selected_fee_60, (decimal?)n.selected_fee_90],
                (decimal?)n.floor_price ?? 0m)));

        return new PlanDrugData(benefits, drugs, marketCosts, networkInfo);
    }

    private static PlanKey Key(object row)
    {
        var r = (IDictionary<string, object?>)row;
        return new PlanKey((string)r["contract_id"]!, (string)r["plan_id"]!, (string)r["segment_id"]!);
    }

    private static decimal Fee(object? value) => value as decimal? ?? 0m;

    private static IReadOnlyDictionary<int, decimal> ByDays(decimal? d30, decimal? d60, decimal? d90)
    {
        var costs = new Dictionary<int, decimal>();
        if (d30 is { } a) costs[30] = a;
        if (d60 is { } b) costs[60] = b;
        if (d90 is { } c) costs[90] = c;
        return costs;
    }

    private static TierCost ToTierCost(dynamic r) => new(
        (CoveragePhase)(short)r.coverage_level, (short)r.tier, (short)r.days_supply,
        [
            Share((short)r.pref_type, (decimal?)r.pref_amt, (decimal?)r.pref_min, (decimal?)r.pref_max),
            Share((short)r.std_type, (decimal?)r.std_amt, (decimal?)r.std_min, (decimal?)r.std_max),
            Share((short)r.mail_pref_type, (decimal?)r.mail_pref_amt, (decimal?)r.mail_pref_min, (decimal?)r.mail_pref_max),
            Share((short)r.mail_std_type, (decimal?)r.mail_std_amt, (decimal?)r.mail_std_min, (decimal?)r.mail_std_max),
        ],
        (bool)r.tier_specialty, (bool)r.ded_applies);

    private static CostShare Share(short type, decimal? amount, decimal? min, decimal? max) =>
        type == 0 ? CostShare.NotOffered : new CostShare((CostType)type, amount ?? 0m, min ?? 0m, max ?? 0m);

    private static InsulinCost ToInsulinCost(dynamic r) => new(
        (int?)(short?)r.tier, (short)r.days_supply,
        [(decimal?)r.copay_pref, (decimal?)r.copay_std, (decimal?)r.copay_mail_pref, (decimal?)r.copay_mail_std],
        [(decimal?)r.coins_pref, (decimal?)r.coins_std, (decimal?)r.coins_mail_pref, (decimal?)r.coins_mail_std]);

    private static PharmacyType PharmacyTypeFrom(string name) => name switch
    {
        "pref" => PharmacyType.PreferredRetail,
        "std" => PharmacyType.StandardRetail,
        "mail_pref" => PharmacyType.PreferredMail,
        "mail_std" => PharmacyType.StandardMail,
        _ => throw new InvalidDataException($"Unknown pharmacy type '{name}'."),
    };

    private static DrugBenefitType? ParseBenefitType(string? value) => value switch
    {
        "Enhanced Alternative" => DrugBenefitType.EnhancedAlternative,
        "Defined Standard" => DrugBenefitType.DefinedStandard,
        "Actuarially Equivalent Standard" => DrugBenefitType.ActuariallyEquivalent,
        "Basic Alternative" => DrugBenefitType.BasicAlternative,
        _ => null,
    };

    private static InvalidOperationException NoRelease(string what) =>
        new($"No active {what} release. Load one with 'ingest' and --activate.");
}
