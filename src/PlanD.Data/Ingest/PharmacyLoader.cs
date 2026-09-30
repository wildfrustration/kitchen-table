using System.IO.Compression;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace PlanD.Data.Ingest;

/// <summary>
/// Loads the pharmacy directory from the NPPES monthly V2 zip into cms.pharmacy without extracting it (the npidata
/// CSV is ~12 GB). Keeps every NPI in an active PDP network plus other active pharmacy NPIs, and uses the DBA name
/// ("CVS PHARMACY #05961") for display. Spec: docs/research/pharmacy-directory.md.
/// </summary>
public sealed partial class PharmacyLoader(NpgsqlDataSource db, Action<string> log)
{
    private static readonly string[] PharmacyTaxonomyPrefixes = ["3336", "332000000X", "332100000X", "332800000X", "332900000X"];
    private static readonly string[] QuotedPrefixes = PharmacyTaxonomyPrefixes.Select(p => "\"" + p).ToArray();

    private sealed record Row(
        string Npi, string LegalName, string? Address1, string? Address2, string? City, string? State, string? Zip5,
        string? Phone, string? PrimaryTaxonomy, bool MailOrder)
    {
        public List<string> OtherNames { get; } = [];
    }

    public async Task<int> LoadAsync(string zipPath, CancellationToken ct = default)
    {
        var label = Path.GetFileNameWithoutExtension(zipPath);
        var releaseId = await Releases.CreateAsync(db, ReleaseSource.Nppes, null, label, zipPath);
        log($"Release {releaseId}: pharmacies from {label}");
        try
        {
            var networkNpis = await NetworkNpis(ct);
            log($"  {networkNpis.Count:N0} NPIs in active PDP networks");

            using var zip = ZipFile.OpenRead(zipPath);
            var rows = ReadProviders(Entry(zip, "npidata_pfile_"), networkNpis);
            log($"  {rows.Count:N0} pharmacies kept ({rows.Keys.Count(networkNpis.Contains):N0} in a PDP network)");
            var dbaCount = ReadOtherNames(Entry(zip, "othername_pfile_"), rows);
            log($"  {dbaCount:N0} other names joined");

            var centroids = await Centroids(ct);
            var written = await Write(releaseId, rows.Values, centroids, ct);

            var missing = networkNpis.Count(n => !rows.ContainsKey(n));
            if (missing > networkNpis.Count / 50)
                throw new InvalidDataException($"{missing:N0} network NPIs are missing from NPPES — wrong file?");
            await Releases.MarkReadyAsync(db, releaseId, new Dictionary<string, long> { ["pharmacy"] = written, ["network_npis_missing"] = missing });
            log($"Release {releaseId} ready: {written:N0} pharmacies; {missing:N0} network NPIs not in NPPES (deactivated)");
            return releaseId;
        }
        catch
        {
            await Releases.MarkFailedAsync(db, releaseId);
            throw;
        }
    }

    private static ZipArchiveEntry Entry(ZipArchive zip, string prefix) =>
        zip.Entries.SingleOrDefault(e => e.Name.StartsWith(prefix, StringComparison.Ordinal) && !e.Name.Contains("fileheader", StringComparison.OrdinalIgnoreCase))
        ?? throw new FileNotFoundException($"No {prefix}*.csv in the NPPES zip.");

    private async Task<HashSet<string>> NetworkNpis(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<string>("""
            select distinct n.npi from cms.spuf_network_pharmacy n
            join cms.release r on r.id = n.release_id and r.source = 'spuf' and r.status = 'active'
            """)).ToHashSet();
    }

    /// <summary>ZIP5 → (lat, lon): the ZIP's own ZCTA, or the ZCTA a PO-box ZIP maps to.</summary>
    private async Task<Dictionary<string, (double Lat, double Lon)>> Centroids(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var geo = await Releases.ActiveAsync(conn, ReleaseSource.Geo, null) ?? throw new InvalidOperationException("No active geo release.");
        var rows = await conn.QueryAsync<(string Zip, double Lat, double Lon)>("""
            select c.zcta, c.lat, c.lon from cms.zcta_centroid c where c.release_id = @geo
            union all
            select z.zip, c.lat, c.lon from cms.zip_zcta z
            join cms.zcta_centroid c on c.release_id = z.release_id and c.zcta = z.zcta
            where z.release_id = @geo and z.zip <> z.zcta
            """, new { geo });
        var map = new Dictionary<string, (double, double)>();
        foreach (var r in rows) map.TryAdd(r.Zip, (r.Lat, r.Lon));
        return map;
    }

    /// <summary>
    /// Every value is double-quoted and embedded quotes are replaced by single quotes, so a line splits on <c>","</c>.
    /// Only candidate lines (network NPI or a pharmacy taxonomy code in the raw text) are split.
    /// </summary>
    private static string[] Split(string line) => line[1..^1].Split("\",\"");

    private Dictionary<string, Row> ReadProviders(ZipArchiveEntry entry, HashSet<string> networkNpis)
    {
        using var reader = new StreamReader(entry.Open(), bufferSize: 1 << 20);
        var header = Split(reader.ReadLine() ?? throw new InvalidDataException("Empty npidata file."));
        int Col(string name) => Array.IndexOf(header, name) is var i and >= 0 ? i : throw new InvalidDataException($"npidata: no '{name}' column.");

        int npi = Col("NPI"), entity = Col("Entity Type Code"), org = Col("Provider Organization Name (Legal Business Name)"),
            last = Col("Provider Last Name (Legal Name)"), first = Col("Provider First Name"),
            addr1 = Col("Provider First Line Business Practice Location Address"),
            addr2 = Col("Provider Second Line Business Practice Location Address"),
            city = Col("Provider Business Practice Location Address City Name"),
            state = Col("Provider Business Practice Location Address State Name"),
            postal = Col("Provider Business Practice Location Address Postal Code"),
            phone = Col("Provider Business Practice Location Address Telephone Number"),
            deactivated = Col("NPI Deactivation Date"), reactivated = Col("NPI Reactivation Date");
        var taxonomies = Enumerable.Range(1, 15).Select(i => (Code: Col($"Healthcare Provider Taxonomy Code_{i}"),
            Primary: Col($"Healthcare Provider Primary Taxonomy Switch_{i}"))).ToArray();

        var rows = new Dictionary<string, Row>();
        long lines = 0;
        while (reader.ReadLine() is { } line)
        {
            if (++lines % 2_000_000 == 0) log($"  npidata: {lines:N0} lines, {rows.Count:N0} kept");
            var candidate = line.Length > 12 && networkNpis.Contains(line.Substring(1, 10))
                            || QuotedPrefixes.Any(p => line.Contains(p, StringComparison.Ordinal));
            if (!candidate) continue;

            var f = Split(line);
            if (f[entity] is not ("1" or "2")) continue; // blank = deactivated
            if (f[deactivated].Length > 0 && f[reactivated].Length == 0) continue;

            var codes = taxonomies.Select(t => (Code: f[t.Code], Primary: f[t.Primary] == "Y")).Where(t => t.Code.Length > 0).ToList();
            var isPharmacy = codes.Any(t => PharmacyTaxonomyPrefixes.Any(p => t.Code.StartsWith(p, StringComparison.Ordinal)));
            if (!isPharmacy && !networkNpis.Contains(f[npi])) continue;

            var legal = f[entity] == "2" ? f[org] : $"{f[first]} {f[last]}".Trim();
            rows[f[npi]] = new Row(
                f[npi], legal, Blank(f[addr1]), Blank(f[addr2]), Blank(f[city]), Blank(f[state]),
                f[postal].Length >= 5 ? f[postal][..5] : null, Blank(f[phone]),
                codes.FirstOrDefault(t => t.Primary).Code ?? codes.FirstOrDefault().Code,
                codes.Any(t => t.Code == "3336M0002X"));
        }
        log($"  npidata: {lines:N0} lines read");
        return rows;
    }

    private static int ReadOtherNames(ZipArchiveEntry entry, Dictionary<string, Row> rows)
    {
        using var reader = new StreamReader(entry.Open(), bufferSize: 1 << 20);
        var header = Split(reader.ReadLine() ?? throw new InvalidDataException("Empty othername file."));
        int npi = Array.IndexOf(header, "NPI"), name = Array.IndexOf(header, "Provider Other Organization Name"),
            type = Array.IndexOf(header, "Provider Other Organization Name Type Code");
        if (npi < 0 || name < 0 || type < 0) throw new InvalidDataException("othername: unexpected header.");

        var n = 0;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length < 12 || !rows.TryGetValue(line.Substring(1, 10), out var row)) continue;
            var f = Split(line);
            // 3 = doing business as, 5 = other name; former legal names (4) would only confuse a search.
            if (f[type] is not ("3" or "5") || f[name].Length == 0 || row.OtherNames.Contains(f[name])) continue;
            if (f[type] == "3") row.OtherNames.Insert(0, f[name]); else row.OtherNames.Add(f[name]);
            n++;
        }
        return n;
    }

    private async Task<long> Write(int releaseId, IEnumerable<Row> rows, Dictionary<string, (double Lat, double Lon)> centroids, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var w = await conn.BeginBinaryImportAsync("""
            copy cms.pharmacy (release_id, npi, display_name, legal_name, store_number, address1, address2, city, state, zip5,
                phone, primary_taxonomy, mail_order, lat, lon, search_text)
            from stdin (format binary)
            """, ct);
        long n = 0;
        foreach (var r in rows)
        {
            var display = r.OtherNames.FirstOrDefault() ?? r.LegalName;
            var store = StoreNumber(display);
            var search = string.Join(' ', new[] { display, r.LegalName, store, r.Address1, r.City }
                    .Concat(r.OtherNames.Skip(1)).Where(s => !string.IsNullOrWhiteSpace(s)))
                .ToLowerInvariant();

            await w.StartRowAsync(ct);
            w.Write(releaseId, NpgsqlDbType.Integer);
            w.WriteText(r.Npi); w.WriteText(display); w.WriteText(r.LegalName); w.WriteText(store);
            w.WriteText(r.Address1); w.WriteText(r.Address2); w.WriteText(r.City); w.WriteText(r.State); w.WriteText(r.Zip5);
            w.WriteText(r.Phone); w.WriteText(r.PrimaryTaxonomy);
            w.Write(r.MailOrder, NpgsqlDbType.Boolean);
            if (r.Zip5 is not null && centroids.TryGetValue(r.Zip5, out var c))
            {
                w.Write(c.Lat, NpgsqlDbType.Double);
                w.Write(c.Lon, NpgsqlDbType.Double);
            }
            else
            {
                w.WriteNull();
                w.WriteNull();
            }
            w.WriteText(search);
            n++;
        }
        await w.CompleteAsync(ct);
        return n;
    }

    private static string? Blank(string s) => s.Length == 0 ? null : s;

    /// <summary>"WALGREENS #01173" → 01173, "WALMART PHARMACY 10-5609" → 10-5609, "CVS PHARMACY 02464" → 02464.</summary>
    private static string? StoreNumber(string name)
    {
        var m = StoreNumberPattern().Match(name);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"(?:#\s*|\s)(\d{1,3}-\d{3,5}|\d{3,6})\s*$")]
    private static partial Regex StoreNumberPattern();
}
