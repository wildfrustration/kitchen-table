using PlanD.Data;
using PlanD.Data.Ingest;

namespace PlanD.Cli;

public static class Cli
{
    private const string Usage = """
        plan-d — Medicare Part D (stand-alone drug plan) quoting

        Database
          db migrate                                  Create or update the schema
          releases                                    List loaded data releases
          activate <releaseId>                        Make a ready release the one quotes use

        Ingest (each load creates a release; add --activate to switch to it when it passes its checks)
          ingest spuf <dir> --year <yyyy> [--label <name>]      Extracted Part D formulary/pharmacy/pricing files
          ingest landscape <csv> --year <yyyy>                  CMS landscape CSV
          ingest geo <dir>                                      ZIP/ZCTA/county/SSA crosswalks (data/raw/geo)
          ingest rxnorm <dir>                                   RxNorm prescribable subset (the rrf folder)

        Quoting
          zip <zip>                                   Counties (SSA codes) a ZIP falls in
          drugs <text> [--year <yyyy>]                Search drugs by name; prints RXCUIs
          quote --zip <zip> --year <yyyy> --drug <rxcui>:<qty>[:<days>] [--drug ...]
                [--county <ssa>] [--mail] [--extra-help 1|2|3] [--start-month <m>]
                [--plan <S1234-001-000> ...] [--top <n>] [--detail <n>]

        Validation
          validate [dir] [--tolerance <usd>]         Compare quotes with captured Plan Finder figures (validation/personas)

        Environment: PLAND_DB overrides the connection string (default: localhost:5442).
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var a = new Args(args);
        await using var db = Db.CreateDataSource();
        Action<string> log = m => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");

        try
        {
            switch (a.Command)
            {
                case "db migrate":
                    var ran = await Migrator.MigrateAsync(db);
                    Console.WriteLine(ran.Count == 0 ? "Schema is up to date." : $"Applied: {string.Join(", ", ran)}");
                    return 0;

                case "releases":
                    foreach (var r in await Releases.ListAsync(db))
                        Console.WriteLine($"{r.Id,4}  {r.Source,-10} {r.PlanYear,-5} {r.Status,-8} {r.Label}");
                    return 0;

                case "activate":
                    await Releases.ActivateAsync(db, int.Parse(a.Positional(0, "releaseId")));
                    Console.WriteLine("Activated.");
                    return 0;

                case "ingest spuf":
                {
                    var dir = a.Positional(0, "dir");
                    var id = await new SpufLoader(db, log).LoadAsync(dir, a.Int("--year"), a.Option("--label") ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(dir)));
                    return await MaybeActivate(db, a, id, log);
                }

                case "ingest landscape":
                {
                    var id = await new LandscapeLoader(db, log).LoadAsync(a.Positional(0, "csv"), a.Int("--year"));
                    return await MaybeActivate(db, a, id, log);
                }

                case "ingest geo":
                {
                    var id = await new GeoLoader(db, log).LoadAsync(a.Positional(0, "dir"));
                    return await MaybeActivate(db, a, id, log);
                }

                case "ingest rxnorm":
                {
                    var id = await new RxNormLoader(db, log).LoadAsync(a.Positional(0, "dir"));
                    return await MaybeActivate(db, a, id, log);
                }

                case "zip":
                    foreach (var c in await new QuoteRepository(db).ResolveZipAsync(a.Positional(0, "zip")))
                        Console.WriteLine($"{c.CountyCode}  {c.CountyName,-30} {c.State,-3} {c.Share:P0}");
                    return 0;

                case "drugs":
                {
                    var text = string.Join(' ', a.Positionals);
                    if (text.Length == 0) throw new ArgumentException("Missing <text>.");
                    foreach (var d in await new QuoteRepository(db).SearchDrugsAsync(text, a.IntOr("--year", 2026)))
                        Console.WriteLine($"{d.Rxcui,-9} {d.TermType,-5} {(d.OnAnyFormulary ? "" : "(not on any formulary) ")}{d.Name}");
                    return 0;
                }

                case "quote":
                    return await QuoteCommand.RunAsync(db, a);

                case "validate":
                    return await ValidateCommand.RunAsync(db, a);

                default:
                    Console.Error.WriteLine($"Unknown command '{string.Join(' ', args)}'.\n\n{Usage}");
                    return 2;
            }
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }

    private static async Task<int> MaybeActivate(Npgsql.NpgsqlDataSource db, Args a, int releaseId, Action<string> log)
    {
        if (!a.Flag("--activate")) return 0;
        await Releases.ActivateAsync(db, releaseId);
        log($"Release {releaseId} is now active.");
        return 0;
    }
}

/// <summary>Tiny argument parser: a one- or two-word command, positionals, --options and --flags.</summary>
public sealed class Args
{
    private static readonly HashSet<string> TwoWord = ["db", "ingest"];
    private readonly List<string> _positional = [];
    private readonly Dictionary<string, List<string>> _options = [];
    private readonly HashSet<string> _flags = [];

    public Args(string[] args)
    {
        var i = 0;
        Command = args[0];
        if (TwoWord.Contains(args[0]) && args.Length > 1) Command += " " + args[++i];

        for (i++; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) _positional.Add(args[i]);
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) && !IsFlag(args[i]))
            {
                if (!_options.TryGetValue(args[i], out var values)) _options[args[i]] = values = [];
                values.Add(args[++i]);
            }
            else _flags.Add(args[i]);
        }
    }

    public string Command { get; }

    public IReadOnlyList<string> Positionals => _positional;

    public string Positional(int index, string name) =>
        index < _positional.Count ? _positional[index] : throw new ArgumentException($"Missing <{name}>.");

    public string? Option(string name) => _options.TryGetValue(name, out var v) ? v[^1] : null;

    public IReadOnlyList<string> Options(string name) => _options.TryGetValue(name, out var v) ? v : [];

    public int IntOr(string name, int fallback) => Option(name) is null ? fallback : Int(name);

    public int Int(string name) =>
        int.TryParse(Option(name), out var v) ? v : throw new ArgumentException($"Missing or invalid {name}.");

    public bool Flag(string name) => _flags.Contains(name);

    private static bool IsFlag(string name) =>
        name is "--activate" or "--mail" or "--json" or "--all";
}
