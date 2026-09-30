using Npgsql;
using NpgsqlTypes;

namespace PlanD.Data.Ingest;

/// <summary>
/// Loads the ZIP → ZCTA → county FIPS → SSA county code crosswalks. Sources and quirks:
/// docs/research/landscape-pbp-geo.md ("ZIP → county" and "SSA ↔ FIPS").
/// </summary>
public sealed class GeoLoader(NpgsqlDataSource db, Action<string> log)
{
    private const string UdsFile = "uds_zip_to_zcta_2022.csv";
    private const string GeocorrFile = "geocorr2022_zcta_to_county_pop20.csv";
    private const string GeocorrCtFile = "geocorr2022_zcta_to_ctcounty_pre2023_pop20.csv";
    private const string NberFile = "ssa_fips_state_county_2026.csv";
    private const string GpciFile = "ffs_2024/CSV/Geographic indices 2020-2026 - Physician GPCI.csv";
    private const string GazetteerFile = "2026_Gaz_zcta_national.txt";

    public async Task<int> LoadAsync(string directory, CancellationToken ct = default)
    {
        var releaseId = await Releases.CreateAsync(db, ReleaseSource.Geo, null, "geocorr2022+uds2022+nber2026+gaz2026", directory);
        log($"Release {releaseId}: geography from {directory}");
        try
        {
            var counts = new Dictionary<string, long>
            {
                ["zip_zcta"] = await LoadZipZcta(releaseId, Path.Combine(directory, UdsFile)),
                ["zcta_county"] = await LoadZctaCounty(releaseId, Path.Combine(directory, GeocorrFile), Path.Combine(directory, GeocorrCtFile)),
                ["fips_ssa"] = await LoadFipsSsa(releaseId, Path.Combine(directory, NberFile), Path.Combine(directory, GpciFile)),
                ["zcta_centroid"] = await LoadCentroids(releaseId, Path.Combine(directory, GazetteerFile)),
            };
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

    private async Task<long> LoadZipZcta(int releaseId, string path)
    {
        using var r = new CsvReader(path);
        int zip = r.Column("zip"), zcta = r.Column("zcta"), po = r.Column("po_name"), state = r.Column("state");
        var rows = new Dictionary<string, (string Zcta, string? Po, string? State)>();
        while (r.Read())
            if (r.Text(zip) is { } z && r.Text(zcta) is { } c)
                rows.TryAdd(z.PadLeft(5, '0'), (c.PadLeft(5, '0'), r.Text(po), r.Text(state)));

        return await Copy("cms.zip_zcta (release_id, zip, zcta, po_name, state)", w =>
        {
            foreach (var (z, v) in rows)
            {
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(z); w.WriteText(v.Zcta); w.WriteText(v.Po); w.WriteText(v.State);
            }
            return rows.Count;
        });
    }

    /// <summary>
    /// Geocorr's main file uses Connecticut's 2022 planning regions (FIPS 091xx); CMS still uses the old
    /// counties, so Connecticut rows come from the pre-2023 county file instead.
    /// </summary>
    private async Task<long> LoadZctaCounty(int releaseId, string mainPath, string ctPath)
    {
        var rows = new Dictionary<(string, string), (string Name, decimal Share)>();

        using (var r = new CsvReader(mainPath, Encodings.Latin1, skipRowsAfterHeader: 1))
        {
            int zcta = r.Column("zcta"), county = r.Column("county"), name = r.Column("CountyName"), share = r.Column("afact");
            while (r.Read())
            {
                var fips = r.Required(county);
                if (r.Text(zcta) is not { } z || fips.StartsWith("091", StringComparison.Ordinal)) continue;
                rows[(z, fips)] = (r.Required(name), r.Decimal(share) ?? 0m);
            }
        }

        using (var r = new CsvReader(ctPath, Encodings.Latin1, skipRowsAfterHeader: 1))
        {
            int zcta = r.Column("zcta"), county = r.Column("CTcounty"), name = r.Column("CTCountyName"), share = r.Column("afact");
            while (r.Read())
                if (r.Text(zcta) is { } z)
                    rows[(z, r.Required(county))] = (r.Required(name), r.Decimal(share) ?? 0m);
        }

        return await Copy("cms.zcta_county (release_id, zcta, county_fips, county_name, share)", w =>
        {
            foreach (var ((z, fips), v) in rows)
            {
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(z); w.WriteText(fips); w.WriteText(v.Name);
                w.Write(v.Share, NpgsqlDbType.Numeric);
            }
            return rows.Count;
        });
    }

    /// <summary>NBER crosswalk, plus Connecticut's old counties and the Virgin Islands from the CMS GPCI file.</summary>
    private async Task<long> LoadFipsSsa(int releaseId, string nberPath, string gpciPath)
    {
        var rows = new Dictionary<(string Fips, string Ssa), (string State, string Name)>();

        using (var r = new CsvReader(nberPath))
        {
            int fips = r.Column("fipscounty"), ssa = r.Column("ssa_code"), state = r.Column("state"), name = r.Column("countyname_fips");
            while (r.Read())
                if (r.Text(fips) is { } f && r.Text(ssa) is { } s)
                    rows.TryAdd((f.PadLeft(5, '0'), s.PadLeft(5, '0')), (r.Text(state) ?? "", r.Text(name) ?? ""));
        }

        using (var r = new CsvReader(gpciPath, skipRowsBeforeHeader: 1))
        {
            int ssa = r.Column("SSACD"), fips = r.Column("FIPSCD"), name = r.Column("County Name");
            while (r.Read())
            {
                if (r.Text(ssa) is not { } s || r.Text(fips) is not { } f) continue;
                var state = s[..2] switch { "07" => "CT", "48" => "VI", _ => null };
                if (state is not null) rows.TryAdd((f.PadLeft(5, '0'), s.PadLeft(5, '0')), (state, r.Text(name) ?? ""));
            }
        }

        return await Copy("cms.fips_ssa (release_id, county_fips, ssa_code, state, county_name)", w =>
        {
            foreach (var ((f, s), v) in rows)
            {
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(f); w.WriteText(s); w.WriteText(v.State); w.WriteText(v.Name);
            }
            return rows.Count;
        });
    }

    /// <summary>Census Gazetteer ZCTA internal points. Recent years are pipe-delimited, older ones tab-delimited.</summary>
    private async Task<long> LoadCentroids(int releaseId, string path)
    {
        var delimiter = File.ReadLines(path).First().Contains('|') ? '|' : '\t';
        using var r = new DelimitedReader(path, delimiter);
        int zcta = r.Column("GEOID"), lat = r.Column("INTPTLAT"), lon = r.Column("INTPTLONG");
        return await Copy("cms.zcta_centroid (release_id, zcta, lat, lon)", w =>
        {
            long n = 0;
            while (r.Read())
            {
                w.StartRow();
                w.Write(releaseId, NpgsqlDbType.Integer);
                w.WriteText(r.Required(zcta));
                w.Write((double)r.Decimal(lat)!.Value, NpgsqlDbType.Double);
                w.Write((double)r.Decimal(lon)!.Value, NpgsqlDbType.Double);
                n++;
            }
            return n;
        });
    }

    private async Task<long> Copy(string target, Func<NpgsqlBinaryImporter, long> writeRows)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var writer = await conn.BeginBinaryImportAsync($"copy {target} from stdin (format binary)");
        var n = writeRows(writer);
        await writer.CompleteAsync();
        return n;
    }
}
