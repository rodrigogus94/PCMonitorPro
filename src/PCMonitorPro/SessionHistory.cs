using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PCMonitorPro;

/// <summary>Coluna do histórico: rótulo legível e unidade (vazia = texto).</summary>
public sealed record HistoryColumn(string Label, string Unit);

/// <summary>Resumo barato de uma sessão (só lê o fim do arquivo).</summary>
public sealed record SessionInfo(string Path, DateTime? Start, string? Last, bool Clean, string? EndNormal);

/// <summary>Resumo completo de uma sessão: máximos, últimos valores e se terminou normalmente.</summary>
public sealed record SessionSummary(string Path, bool Clean, int Rows, string? First, string? LastTs,
    Dictionary<string, (string Label, double Peak)> Max, Dictionary<string, (string Label, string Value)> Last);

public sealed record LastValues(string Ts, Dictionary<string, double> Values, DateTime? Start = null);

public sealed record RecentStats(int Rows, double Hours, Dictionary<string, (double Max, double Avg)> Temps);

/// <summary>
/// Histórico das leituras em CSV (uma sessão por arquivo). Cada linha é gravada e sincronizada em disco, para sobreviver
/// a uma tela azul. Sessões que não terminam com a marca "# fim-normal" são tratadas como quedas.
/// </summary>
public sealed class HistoryLogger : IDisposable
{
    public const string TsFormat = "yyyy-MM-dd HH:mm:ss";
    public const string EndMark = "# fim-normal";

    private readonly string _folder;
    private FileStream? _file;
    private StreamWriter? _writer;
    private List<string> _ids = new();

    public HistoryLogger(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    public string? Path { get; private set; }
    public bool Active => _writer != null;

    public void Start(IReadOnlyDictionary<string, HistoryColumn> catalog, DateTime? now = null)
    {
        Close();
        var t = now ?? DateTime.Now;
        _ids = catalog.Keys.ToList();
        Path = System.IO.Path.Combine(_folder, $"sessao-{t:yyyyMMdd-HHmmss}.csv");
        // FileShare.Read: o arquivo pode ser lido (ex.: aba de histórico) enquanto a sessão grava.
        _file = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(_file, new UTF8Encoding(false)) { NewLine = "\n" };
        var legend = catalog.ToDictionary(kv => kv.Key,
            kv => $"{kv.Value.Label} [{(string.IsNullOrEmpty(kv.Value.Unit) ? "texto" : kv.Value.Unit)}]");
        var json = JsonSerializer.Serialize(legend, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        _writer.WriteLine($"# inicio={t.ToString(TsFormat, CultureInfo.InvariantCulture)}");
        _writer.WriteLine("# legenda=" + json);
        _writer.WriteLine(Csv.Join(new[] { "timestamp" }.Concat(_ids)));
        Sync();
    }

    private void Sync()
    {
        try { _writer?.Flush(); _file?.Flush(true); }
        catch (IOException ex) { AppLog.Warn("Histórico: falha ao sincronizar", ex); }
    }

    /// <summary>Valores: número (double/float/int), texto ou null (célula vazia).</summary>
    public void Write(IReadOnlyDictionary<string, object?> values, DateTime? now = null)
    {
        if (_writer == null) return;
        var t = now ?? DateTime.Now;
        var row = new List<string> { t.ToString(TsFormat, CultureInfo.InvariantCulture) };
        foreach (var id in _ids)
            row.Add(values.TryGetValue(id, out var v) ? FormatValue(v) : "");
        try { _writer.WriteLine(Csv.Join(row)); }
        catch (IOException ex) { AppLog.Warn("Histórico: falha ao gravar", ex); return; }
        Sync();
    }

    public static string FormatValue(object? v) => v switch
    {
        null => "",
        double d => d.ToString("0.00", CultureInfo.InvariantCulture),
        float f => ((double)f).ToString("0.00", CultureInfo.InvariantCulture),
        IFormattable fm => fm.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };

    public void Close(DateTime? now = null)
    {
        if (_writer != null)
        {
            try
            {
                _writer.WriteLine($"{EndMark}={(now ?? DateTime.Now).ToString(TsFormat, CultureInfo.InvariantCulture)}");
                Sync();
                _writer.Dispose();
            }
            catch (IOException ex) { AppLog.Warn("Histórico: falha ao fechar a sessão", ex); }
        }
        _writer = null; _file = null;
    }

    public void Dispose() => Close();

    // ---------------------------------------------------------------- leitura
    public static List<string> ListSessions(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "sessao-*.csv")
                .OrderByDescending(p => System.IO.Path.GetFileName(p), StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(); }
    }

    private static List<string> TailLines(string path, int size = 8192)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, f.Length - size);
        f.Seek(start, SeekOrigin.Begin);
        var buf = new byte[f.Length - start];
        int n = 0;
        while (n < buf.Length) { int r = f.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; }
        var text = Encoding.UTF8.GetString(buf, 0, n);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
        // Se começamos no meio do arquivo, a primeira linha pode estar cortada.
        if (start > 0 && lines.Count > 0) lines.RemoveAt(0);
        return lines;
    }

    private static bool IsDataLine(string ln) => !ln.StartsWith('#') && ln.Length >= 4 && char.IsDigit(ln[0]) && char.IsDigit(ln[1]) && char.IsDigit(ln[2]) && char.IsDigit(ln[3]);

    public static SessionInfo QuickInfo(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        DateTime? start = null;
        if (name.Length >= 22 && DateTime.TryParseExact(name.Substring(7, 15), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            start = d;
        List<string> lines;
        try { lines = TailLines(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(path, start, null, false, null); }
        bool clean = false; string? endNormal = null, last = null;
        if (lines.Count > 0 && lines[^1].StartsWith(EndMark))
        {
            clean = true;
            var l = lines[^1]; endNormal = l[(l.IndexOf('=') + 1)..];
            lines.RemoveAt(lines.Count - 1);
        }
        for (int i = lines.Count - 1; i >= 0; i--)
            if (IsDataLine(lines[i])) { last = Csv.Split(lines[i])[0]; break; }
        return new(path, start, last, clean, endNormal);
    }

    public static SessionSummary Summarize(string path)
    {
        var legend = new Dictionary<string, string>();
        List<string>? header = null; var rows = new List<List<string>>(); bool clean = false;
        foreach (var raw in ReadAllLinesShared(path))
        {
            var ln = raw.TrimEnd('\r');
            if (ln.StartsWith("# legenda="))
            {
                try { legend = JsonSerializer.Deserialize<Dictionary<string, string>>(ln[(ln.IndexOf('=') + 1)..]) ?? new(); }
                catch (JsonException) { }
            }
            else if (ln.StartsWith(EndMark)) clean = true;
            else if (ln.StartsWith('#') || ln.Trim().Length == 0) continue;
            else if (header == null) header = Csv.Split(ln);
            else { var r = Csv.Split(ln); if (r.Count == header.Count) rows.Add(r); }
        }
        var max = new Dictionary<string, (string, double)>(); var last = new Dictionary<string, (string, string)>();
        if (header == null || rows.Count == 0)
            return new(path, clean, 0, null, null, max, last);
        for (int i = 1; i < header.Count; i++)
        {
            var col = header[i]; var label = legend.TryGetValue(col, out var lb) ? lb : col;
            double? peak = null;
            foreach (var r in rows)
                if (double.TryParse(r[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && (peak == null || x > peak)) peak = x;
            if (peak != null) max[col] = (label, peak.Value);
            if (rows[^1][i] != "") last[col] = (label, rows[^1][i]);
        }
        return new(path, clean, rows.Count, rows[0][0], rows[^1][0], max, last);
    }

    private static IEnumerable<string> ReadAllLinesShared(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(f, Encoding.UTF8);
        string? l;
        while ((l = r.ReadLine()) != null) yield return l;
    }

    private static readonly string[] KeyOrder = { "cpu.temp", "cpu.load", "ram.pct", "nv0.temp", "nv0.load", "proc.top" };
    private static readonly Dictionary<string, string> KeyText = new()
    {
        ["cpu.temp"] = "CPU {0} °C", ["cpu.load"] = "uso CPU {0}%", ["ram.pct"] = "RAM {0}%",
        ["nv0.temp"] = "GPU {0} °C", ["nv0.load"] = "uso GPU {0}%", ["proc.top"] = "maior RAM: {0}"
    };

    /// <summary>Texto curto com os últimos valores relevantes (ex.: "CPU 91 °C, RAM 88%").</summary>
    public static string DescribeLast(SessionSummary s)
    {
        var parts = new List<string>();
        foreach (var k in KeyOrder)
        {
            if (!s.Last.TryGetValue(k, out var v)) continue;
            var val = double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d.ToString("0", CultureInfo.InvariantCulture) : v.Value;
            parts.Add(string.Format(KeyText[k], val));
        }
        return string.Join(", ", parts);
    }

    /// <summary>Última sessão que não terminou normalmente (ou null). Só olha a mais recente.</summary>
    public static SessionInfo? FindUnclean(string folder)
    {
        foreach (var p in ListSessions(folder).Take(1))
        {
            var info = QuickInfo(p);
            if (!info.Clean && info.Last != null) return info;
        }
        return null;
    }

    /// <summary>Cabeçalho + última linha de dados, de forma barata.</summary>
    public static LastValues? ReadLastValues(string path)
    {
        List<string>? header = null; List<string> tail;
        try
        {
            foreach (var ln in ReadAllLinesShared(path))
            {
                if (ln.StartsWith('#') || ln.Trim().Length == 0) continue;
                header = Csv.Split(ln); break;
            }
            tail = TailLines(path).Where(IsDataLine).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        if (header == null || tail.Count == 0) return null;
        var row = Csv.Split(tail[^1]);
        if (row.Count != header.Count) return null;
        var values = new Dictionary<string, double>();
        for (int i = 1; i < header.Count; i++)
            if (double.TryParse(row[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) values[header[i]] = d;
        return new(row[0], values);
    }

    /// <summary>Últimos valores de cada sessão que não terminou normalmente (exceto a sessão em andamento).</summary>
    public static List<LastValues> UncleanLastValues(string folder, string? exclude = null, int limit = 15)
    {
        var outList = new List<LastValues>();
        foreach (var p in ListSessions(folder))
        {
            if (outList.Count >= limit) break;
            if (exclude != null && string.Equals(System.IO.Path.GetFullPath(p), System.IO.Path.GetFullPath(exclude), StringComparison.OrdinalIgnoreCase)) continue;
            var info = QuickInfo(p);
            if (info.Clean) continue;
            var lv = ReadLastValues(p);
            if (lv != null) outList.Add(lv with { Start = info.Start });
        }
        return outList;
    }

    /// <summary>Duração real (s) entre a primeira e a última linha (0 se não der para ler).</summary>
    public static double SpanSeconds(string? first, string? last)
    {
        if (!DateTime.TryParseExact(first, TsFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)
            || !DateTime.TryParseExact(last, TsFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var b)) return 0;
        return Math.Max(0, (b - a).TotalSeconds);
    }

    /// <summary>Máx./média de cpu.temp e nv0.temp nas sessões das últimas <paramref name="hours"/> horas.</summary>
    public static RecentStats ReadRecentStats(string folder, double hours = 24, DateTime? now = null)
    {
        var cutoff = (now ?? DateTime.Now).AddHours(-hours);
        var acc = new Dictionary<string, List<double>> { ["cpu.temp"] = new(), ["nv0.temp"] = new() };
        int rows = 0; double seconds = 0;
        foreach (var p in ListSessions(folder))
        {
            try
            {
                if (File.GetLastWriteTime(p) < cutoff) break;
                List<string>? header = null; Dictionary<string, int> idx = new();
                string? first = null, last = null;
                foreach (var ln in ReadAllLinesShared(p))
                {
                    if (ln.StartsWith('#') || ln.Trim().Length == 0) continue;
                    if (header == null)
                    {
                        header = Csv.Split(ln);
                        foreach (var k in acc.Keys) { int i = header.IndexOf(k); if (i >= 0) idx[k] = i; }
                        continue;
                    }
                    var row = Csv.Split(ln);
                    if (row.Count != header.Count) continue;
                    rows++; last = row[0]; first ??= row[0];
                    foreach (var (k, i) in idx)
                        if (double.TryParse(row[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) acc[k].Add(d);
                }
                seconds += SpanSeconds(first, last);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var temps = new Dictionary<string, (double, double)>();
        foreach (var (k, v) in acc) if (v.Count > 0) temps[k] = (v.Max(), v.Average());
        return new(rows, seconds / 3600, temps);
    }

    /// <summary>Apaga sessões mais antigas que <paramref name="days"/> dias. Retorna quantas foram apagadas.</summary>
    public static int Cleanup(string folder, int days, DateTime? now = null)
    {
        var limit = (now ?? DateTime.Now).AddDays(-days); int n = 0;
        foreach (var p in ListSessions(folder))
        {
            try { if (File.GetLastWriteTime(p) < limit) { File.Delete(p); n++; } }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Warn("Histórico: não apagou " + p, ex); }
        }
        return n;
    }
}

/// <summary>CSV mínimo (RFC 4180): aspas só quando há vírgula, aspas ou quebra de linha.</summary>
internal static class Csv
{
    public static string Join(IEnumerable<string> cells) => string.Join(",", cells.Select(Quote));

    private static string Quote(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";

    public static List<string> Split(string line)
    {
        var res = new List<string>(); var sb = new StringBuilder(); bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q)
            {
                if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                else sb.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == ',') { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        res.Add(sb.ToString());
        return res;
    }
}
