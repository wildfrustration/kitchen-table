using Npgsql;
using NpgsqlTypes;

namespace PlanD.Data.Ingest;

/// <summary>
/// Loads the RxNorm "prescribable" subset (no license needed): concept names and term types from RXNCONSO,
/// and NDC → RXCUI from RXNSAT. RRF files are pipe-delimited with a trailing pipe and no header.
/// </summary>
public sealed class RxNormLoader(NpgsqlDataSource db, Action<string> log)
{
    // Synonym atoms; each concept also has one atom with its primary term type.
    private static readonly HashSet<string> SynonymTypes = ["SY", "TMSY", "PSN", "ET"];

    public async Task<int> LoadAsync(string rrfDirectory, CancellationToken ct = default)
    {
        var label = Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(rrfDirectory))) ?? "rxnorm";
        var releaseId = await Releases.CreateAsync(db, ReleaseSource.RxNorm, null, label, rrfDirectory);
        log($"Release {releaseId}: RxNorm from {rrfDirectory}");
        try
        {
            var concepts = ReadConcepts(Path.Combine(rrfDirectory, "RXNCONSO.RRF"));
            var ndcs = ReadNdcs(Path.Combine(rrfDirectory, "RXNSAT.RRF"));
            var generics = ReadGenerics(Path.Combine(rrfDirectory, "RXNREL.RRF"), concepts);

            await using var conn = await db.OpenConnectionAsync(ct);
            await using (var w = await conn.BeginBinaryImportAsync("copy cms.rx_concept (release_id, rxcui, tty, name) from stdin (format binary)", ct))
            {
                foreach (var (rxcui, (tty, name)) in concepts)
                {
                    w.StartRow();
                    w.Write(releaseId, NpgsqlDbType.Integer);
                    w.WriteText(rxcui); w.WriteText(tty); w.WriteText(name);
                }
                await w.CompleteAsync(ct);
            }
            await using (var w = await conn.BeginBinaryImportAsync("copy cms.rx_ndc (release_id, ndc, rxcui) from stdin (format binary)", ct))
            {
                foreach (var (ndc, rxcui) in ndcs)
                {
                    w.StartRow();
                    w.Write(releaseId, NpgsqlDbType.Integer);
                    w.WriteText(ndc); w.WriteText(rxcui);
                }
                await w.CompleteAsync(ct);
            }

            await using (var w = await conn.BeginBinaryImportAsync("copy cms.rx_generic (release_id, brand_rxcui, generic_rxcui) from stdin (format binary)", ct))
            {
                foreach (var (brand, generic) in generics)
                {
                    w.StartRow();
                    w.Write(releaseId, NpgsqlDbType.Integer);
                    w.WriteText(brand); w.WriteText(generic);
                }
                await w.CompleteAsync(ct);
            }

            var counts = new Dictionary<string, long>
            {
                ["rx_concept"] = concepts.Count, ["rx_ndc"] = ndcs.Count, ["rx_generic"] = generics.Count,
            };
            await Releases.MarkReadyAsync(db, releaseId, counts);
            log($"Release {releaseId} ready: {concepts.Count:N0} concepts, {ndcs.Count:N0} NDCs, {generics.Count:N0} brand→generic pairs");
            return releaseId;
        }
        catch
        {
            await Releases.MarkFailedAsync(db, releaseId);
            throw;
        }
    }

    private static Dictionary<string, (string Tty, string Name)> ReadConcepts(string path)
    {
        // RXCUI|LAT|TS|LUI|STT|SUI|ISPREF|RXAUI|SAUI|SCUI|SDUI|SAB|TTY|CODE|STR|SRL|SUPPRESS|CVF|
        var concepts = new Dictionary<string, (string, string)>();
        foreach (var line in File.ReadLines(path))
        {
            var f = line.Split('|');
            if (f[11] != "RXNORM" || f[16] != "N" || SynonymTypes.Contains(f[12])) continue;
            concepts.TryAdd(f[0], (f[12], f[14]));
        }
        return concepts;
    }

    /// <summary>
    /// Branded products and their generic equivalents. In RXNREL, "RXCUI1 tradename_of RXCUI2" means RXCUI2
    /// (the brand) is a trade name of RXCUI1 (the generic).
    /// </summary>
    private static HashSet<(string Brand, string Generic)> ReadGenerics(string path, Dictionary<string, (string Tty, string Name)> concepts)
    {
        // RXCUI1|RXAUI1|STYPE1|REL|RXCUI2|RXAUI2|STYPE2|RELA|RUI|SRUI|SAB|SL|DIR|RG|SUPPRESS|CVF|
        var pairs = new HashSet<(string, string)>();
        foreach (var line in File.ReadLines(path))
        {
            var f = line.Split('|');
            if (f[7] != "tradename_of" || f[10] != "RXNORM") continue;
            if (!concepts.TryGetValue(f[0], out var generic) || !concepts.TryGetValue(f[4], out var brand)) continue;
            if ((brand.Tty, generic.Tty) is ("SBD", "SCD") or ("BPCK", "GPCK")) pairs.Add((f[4], f[0]));
        }
        return pairs;
    }

    private static HashSet<(string Ndc, string Rxcui)> ReadNdcs(string path)
    {
        // RXCUI|LUI|SUI|RXAUI|STYPE|CODE|ATUI|SATUI|ATN|SAB|ATV|SUPPRESS|CVF|
        var ndcs = new HashSet<(string, string)>();
        foreach (var line in File.ReadLines(path))
        {
            var f = line.Split('|');
            if (f[8] == "NDC" && f[9] == "RXNORM" && f[11] == "N") ndcs.Add((f[10], f[0]));
        }
        return ndcs;
    }
}
