using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace PlanD.Data.Ingest;

/// <summary>
/// Loads the PDP rows of the CMS landscape CSV (one combined MA + PDP file since CY2025) into
/// cms.landscape_plan, one row per plan. Columns are matched by name because the header changes between years.
/// </summary>
public sealed class LandscapeLoader(NpgsqlDataSource db, Action<string> log)
{
    public async Task<int> LoadAsync(string csvPath, int planYear, CancellationToken ct = default)
    {
        var label = Path.GetFileNameWithoutExtension(csvPath);
        var releaseId = await Releases.CreateAsync(db, ReleaseSource.Landscape, planYear, label, csvPath);
        log($"Release {releaseId}: {label} (plan year {planYear})");

        try
        {
            var rows = Read(csvPath, planYear);
            await using var conn = await db.OpenConnectionAsync(ct);
            await using (var w = await conn.BeginBinaryImportAsync("""
                copy cms.landscape_plan (release_id, contract_id, plan_id, segment_id, organization_name, parent_organization,
                    plan_name, drug_benefit_type, part_d_deductible, part_d_basic_premium, part_d_supplemental_premium,
                    part_d_total_premium, part_d_lis_premium, oop_threshold, star_part_d, lis_benchmark, pdp_region_code)
                from stdin (format binary)
                """, ct))
            {
                foreach (var p in rows)
                {
                    await w.StartRowAsync(ct);
                    w.Write(releaseId, NpgsqlDbType.Integer);
                    w.WriteText(p.ContractId); w.WriteText(p.PlanId); w.WriteText(p.SegmentId);
                    w.WriteText(p.Organization); w.WriteText(p.Parent); w.WriteText(p.PlanName); w.WriteText(p.BenefitType);
                    w.WriteNumeric(p.Deductible); w.WriteNumeric(p.Basic); w.WriteNumeric(p.Supplemental);
                    w.WriteNumeric(p.Total); w.WriteNumeric(p.Lis); w.WriteNumeric(p.OopThreshold);
                    w.WriteText(p.Stars);
                    if (p.LisBenchmark is { } b) w.Write(b, NpgsqlDbType.Boolean); else w.WriteNull();
                    w.WriteText(p.PdpRegion);
                }
                await w.CompleteAsync(ct);
            }

            if (rows.Count < 100) throw new InvalidDataException($"Only {rows.Count} PDPs in {csvPath}.");
            await Releases.MarkReadyAsync(db, releaseId, new Dictionary<string, long> { ["landscape_plan"] = rows.Count });
            log($"Release {releaseId} ready: {rows.Count:N0} PDPs");
            return releaseId;
        }
        catch
        {
            await Releases.MarkFailedAsync(db, releaseId);
            throw;
        }
    }

    private static List<PdpRow> Read(string path, int planYear)
    {
        using var r = new CsvReader(path);
        int year = r.Column("Contract Year"), category = r.Column("Contract Category Type"), contract = r.Column("Contract ID"),
            plan = r.Column("Plan ID"), segment = r.Column("Segment ID"), org = r.Column("Organization Marketing Name"),
            parent = r.Column("Parent Organization Name"), planName = r.Column("Plan Name"), benefit = r.Column("Drug Benefit Type"),
            deductible = r.Column("Annual Part D Deductible Amount"), basic = r.Column("Part D Basic Premium"),
            supplemental = r.Column("Part D Supplemental Premium"), total = r.Column("Part D Total Premium"),
            lis = r.Column("Part D Low Income Beneficiary Premium Amount"), oop = r.Column("Part D Out-of-Pocket (OOP) Threshold"),
            stars = r.Column("Part D Summary Star Rating"), bench = r.Column("Part D Basic Premium At or Below Regional Benchmark"),
            pdpRegion = r.Column("PDP Region Code");

        var plans = new Dictionary<(string, string, string), PdpRow>();
        while (r.Read())
        {
            if (r.Int(year) != planYear)
                throw new InvalidDataException($"{Path.GetFileName(path)} row {r.Row}: contract year {r.Text(year)}, expected {planYear}.");
            if (r.Text(category) != "PDP") continue;

            var key = (r.Required(contract), r.Required(plan).PadLeft(3, '0'), r.Required(segment).PadLeft(3, '0'));
            if (plans.ContainsKey(key)) continue; // PDPs repeat once per state in their region

            plans[key] = new PdpRow(
                key.Item1, key.Item2, key.Item3,
                NullIfNa(r.Text(org)), NullIfNa(r.Text(parent)), r.Required(planName), NullIfNa(r.Text(benefit)),
                Money(r, deductible), Money(r, basic), Money(r, supplemental), Money(r, total), Money(r, lis), Money(r, oop),
                NullIfNa(r.Text(stars)),
                r.Text(bench) switch { "Yes" => true, "No" => false, _ => null },
                NullIfNa(r.Text(pdpRegion)));
        }
        return plans.Values.ToList();
    }

    private static string? NullIfNa(string? value) => value is null or "Not Applicable" ? null : value;

    /// <summary>"$1,234.00 ", "($60.80)", "Not Applicable" or empty.</summary>
    private static decimal? Money(RowReader r, int column)
    {
        var s = r.Text(column);
        if (s is null or "Not Applicable") return null;
        var negative = s.StartsWith('(') && s.EndsWith(')');
        var digits = s.Trim('(', ')').Replace("$", "").Replace(",", "").Trim();
        if (digits.Length == 0) return null;
        var value = decimal.Parse(digits, NumberStyles.Float, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    private sealed record PdpRow(
        string ContractId, string PlanId, string SegmentId, string? Organization, string? Parent, string PlanName,
        string? BenefitType, decimal? Deductible, decimal? Basic, decimal? Supplemental, decimal? Total, decimal? Lis,
        decimal? OopThreshold, string? Stars, bool? LisBenchmark, string? PdpRegion);
}
