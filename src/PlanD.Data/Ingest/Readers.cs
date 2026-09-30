using System.Globalization;
using System.Text;

namespace PlanD.Data.Ingest;

/// <summary>Column access shared by the pipe/tab and CSV readers.</summary>
public abstract class RowReader : IDisposable
{
    protected RowReader(string path) => Path = path;

    public string Path { get; }
    public abstract IReadOnlyList<string> Header { get; }
    public long Row { get; protected set; }

    public abstract bool Read();
    protected abstract ReadOnlySpan<char> Raw(int column);
    public abstract void Dispose();

    public int Column(params string[] names)
    {
        foreach (var name in names)
        {
            var i = OptionalColumn(name);
            if (i is not null) return i.Value;
        }
        throw new InvalidDataException(
            $"{System.IO.Path.GetFileName(Path)}: none of [{string.Join(", ", names)}] in header [{string.Join(", ", Header)}].");
    }

    public int? OptionalColumn(string name)
    {
        for (var i = 0; i < Header.Count; i++)
            if (string.Equals(Header[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        return null;
    }

    public ReadOnlySpan<char> Span(int column) => Raw(column).Trim();

    /// <summary>The trimmed value, or null for the files' missing markers (empty, a space, or SAS ".").</summary>
    public string? Text(int column)
    {
        var s = Span(column);
        return s.IsEmpty || s is "." ? null : s.ToString();
    }

    public string Required(int column) =>
        Text(column) ?? throw new InvalidDataException(
            $"{System.IO.Path.GetFileName(Path)} row {Row}: '{Header[column]}' is empty.");

    public decimal? Decimal(int column)
    {
        var s = Span(column);
        if (s.IsEmpty || s is ".") return null;
        return decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public int? Int(int column)
    {
        var s = Span(column);
        if (s.IsEmpty || s is ".") return null;
        return int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    /// <summary>Y/N and 1/0 flags.</summary>
    public bool Flag(int column)
    {
        var s = Span(column);
        return s is "Y" or "y" or "1" or "Yes" or "YES";
    }

    protected static FileStream OpenSequential(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
}

/// <summary>
/// Header + rows separated by a single character with no quoting — the CMS pipe (SPUF) and tab (PBP) files.
/// Fails fast on a row whose field count doesn't match the header.
/// </summary>
public sealed class DelimitedReader : RowReader
{
    private readonly StreamReader _reader;
    private readonly char _delimiter;
    private readonly string[] _header;
    private readonly int[] _starts;
    private readonly int[] _lengths;
    private string _line = "";

    public DelimitedReader(string path, char delimiter, Encoding? encoding = null) : base(path)
    {
        _delimiter = delimiter;
        _reader = new StreamReader(OpenSequential(path), encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1 << 20);
        var header = _reader.ReadLine() ?? throw new InvalidDataException($"{path} is empty.");
        _header = header.Split(delimiter).Select(h => h.Trim()).ToArray();
        _starts = new int[_header.Length];
        _lengths = new int[_header.Length];
    }

    public override IReadOnlyList<string> Header => _header;

    public override bool Read()
    {
        while (_reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            Row++;
            _line = line;

            var field = 0;
            var start = 0;
            for (var i = 0; i <= line.Length; i++)
            {
                if (i < line.Length && line[i] != _delimiter) continue;
                if (field >= _header.Length) Mismatch(field + 1);
                _starts[field] = start;
                _lengths[field] = i - start;
                field++;
                start = i + 1;
            }
            if (field != _header.Length) Mismatch(field);
            return true;
        }
        return false;
    }

    private void Mismatch(int fields) => throw new InvalidDataException(
        $"{System.IO.Path.GetFileName(Path)} row {Row}: expected {_header.Length} fields, found {fields}.");

    protected override ReadOnlySpan<char> Raw(int column) => _line.AsSpan(_starts[column], _lengths[column]);

    /// <summary>The raw text from the start of one column to the end of a later one, delimiters included.</summary>
    public ReadOnlySpan<char> Range(int first, int last) =>
        _line.AsSpan(_starts[first], _starts[last] + _lengths[last] - _starts[first]);

    public override void Dispose() => _reader.Dispose();
}

/// <summary>RFC 4180 CSV (quoted fields, "" escapes, quoted line breaks). For the small CSV sources.</summary>
public sealed class CsvReader : RowReader
{
    private readonly StreamReader _reader;
    private readonly string[] _header;
    private List<string> _fields = [];

    /// <param name="skipRowsAfterHeader">Extra label rows under the header (Geocorr has one).</param>
    public CsvReader(string path, Encoding? encoding = null, int skipRowsBeforeHeader = 0, int skipRowsAfterHeader = 0) : base(path)
    {
        _reader = new StreamReader(OpenSequential(path), encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1 << 16);
        for (var i = 0; i < skipRowsBeforeHeader; i++) ReadRecord();
        _header = (ReadRecord() ?? throw new InvalidDataException($"{path} is empty.")).Select(h => h.Trim()).ToArray();
        for (var i = 0; i < skipRowsAfterHeader; i++) ReadRecord();
    }

    public override IReadOnlyList<string> Header => _header;

    public override bool Read()
    {
        while (ReadRecord() is { } record)
        {
            if (record.Count == 1 && record[0].Length == 0) continue;
            Row++;
            if (record.Count != _header.Length)
                throw new InvalidDataException(
                    $"{System.IO.Path.GetFileName(Path)} row {Row}: expected {_header.Length} fields, found {record.Count}.");
            _fields = record;
            return true;
        }
        return false;
    }

    protected override ReadOnlySpan<char> Raw(int column) => _fields[column];

    private List<string>? ReadRecord()
    {
        var line = _reader.ReadLine();
        if (line is null) return null;

        var fields = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        var i = 0;
        while (true)
        {
            if (i == line.Length)
            {
                if (quoted)
                {
                    // A quoted field spans lines.
                    var next = _reader.ReadLine() ?? throw new InvalidDataException($"{Path}: unterminated quote.");
                    sb.Append('\n');
                    line = next;
                    i = 0;
                    continue;
                }
                fields.Add(sb.ToString());
                return fields;
            }

            var c = line[i++];
            if (quoted)
            {
                if (c != '"') sb.Append(c);
                else if (i < line.Length && line[i] == '"') { sb.Append('"'); i++; }
                else quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
    }

    public override void Dispose() => _reader.Dispose();
}

public static class Encodings
{
    // Field initializers run in order: register the code-page provider before asking for 1252.
    private static readonly bool Registered = Register();

    /// <summary>The SPUF plan file and several PBP tables are Windows-1252, not UTF-8.</summary>
    public static Encoding Windows1252 { get; } = Encoding.GetEncoding(1252);

    private static bool Register()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    }

    public static Encoding Latin1 => Encoding.Latin1;
}
