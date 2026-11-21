using Xunit;

namespace PCMonitorPro.Tests;

[Collection("pasta-de-dados")]
public class HistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pmp-hist-" + Guid.NewGuid().ToString("N"));
    public HistoryTests() { Directory.CreateDirectory(_dir); AppLog.UsePath(Path.Combine(_dir, "t.log")); }
    public void Dispose() { AppLog.UsePath(null); try { Directory.Delete(_dir, true); } catch { } }

    private static readonly Dictionary<string, HistoryColumn> Catalog = new()
    {
        ["cpu.temp"] = new("CPU temperatura", "°C"),
        ["ram.pct"] = new("RAM", "%"),
        ["proc.top"] = new("Maior RAM", ""),
    };

    private static Dictionary<string, object?> Vals(double cpu, double ram, string top) =>
        new() { ["cpu.temp"] = cpu, ["ram.pct"] = ram, ["proc.top"] = top };

    [Fact]
    public void CleanSession_HasEndMark_AndIsNotUnclean()
    {
        using var h = new HistoryLogger(_dir);
        h.Start(Catalog, new DateTime(2026, 1, 2, 3, 4, 5));
        h.Write(Vals(60, 40, "chrome"), new DateTime(2026, 1, 2, 3, 4, 10));
        var path = h.Path!; h.Close();
        var info = HistoryLogger.QuickInfo(path);
        Assert.True(info.Clean);
        Assert.Equal("2026-01-02 03:04:10", info.Last);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), info.Start);
        Assert.Null(HistoryLogger.FindUnclean(_dir));
    }

    [Fact]
    public void SessionWithoutEndMark_IsDetectedAsCrash_WithLastValues()
    {
        var h = new HistoryLogger(_dir);
        h.Start(Catalog, new DateTime(2026, 1, 2, 3, 4, 5));
        h.Write(Vals(95.5, 91, "game, \"x\".exe"), new DateTime(2026, 1, 2, 3, 4, 10));
        // sem Close(): simula queda; o arquivo continua aberto para leitura compartilhada
        var crash = HistoryLogger.FindUnclean(_dir);
        Assert.NotNull(crash);
        Assert.Equal("2026-01-02 03:04:10", crash!.Last);
        var lv = HistoryLogger.ReadLastValues(crash.Path)!;
        Assert.Equal(95.5, lv.Values["cpu.temp"]);
        Assert.Equal(91, lv.Values["ram.pct"]);
        var sum = HistoryLogger.Summarize(crash.Path);
        Assert.False(sum.Clean);
        Assert.Equal("game, \"x\".exe", sum.Last["proc.top"].Value);   // CSV com vírgula e aspas
        Assert.Contains("CPU 96 °C", HistoryLogger.DescribeLast(sum));  // 95,5 arredonda para 96
        Assert.Single(HistoryLogger.UncleanLastValues(_dir));
        Assert.Empty(HistoryLogger.UncleanLastValues(_dir, exclude: crash.Path));
        h.Dispose();
    }

    [Fact]
    public void Summarize_ComputesPeaks()
    {
        using var h = new HistoryLogger(_dir);
        h.Start(Catalog);
        h.Write(Vals(50, 30, "a")); h.Write(Vals(88, 35, "b")); h.Write(Vals(70, 33, "c"));
        h.Close();
        var s = HistoryLogger.Summarize(HistoryLogger.ListSessions(_dir)[0]);
        Assert.True(s.Clean);
        Assert.Equal(3, s.Rows);
        Assert.Equal(88, s.Max["cpu.temp"].Peak);
        Assert.StartsWith("CPU temperatura", s.Max["cpu.temp"].Label);
    }

    [Fact]
    public void SpanSeconds_AndRecentStats()
    {
        Assert.Equal(60, HistoryLogger.SpanSeconds("2026-01-01 10:00:00", "2026-01-01 10:01:00"));
        Assert.Equal(0, HistoryLogger.SpanSeconds("x", "2026-01-01 10:01:00"));
        using var h = new HistoryLogger(_dir);
        h.Start(Catalog, new DateTime(2026, 1, 1, 10, 0, 0));
        h.Write(Vals(40, 1, "a"), new DateTime(2026, 1, 1, 10, 0, 0));
        h.Write(Vals(80, 1, "a"), new DateTime(2026, 1, 1, 11, 0, 0));
        h.Close();
        var st = HistoryLogger.ReadRecentStats(_dir, 24);
        Assert.Equal(2, st.Rows);
        Assert.Equal(1.0, st.Hours, 3);
        Assert.Equal(80, st.Temps["cpu.temp"].Max);
        Assert.Equal(60, st.Temps["cpu.temp"].Avg);
    }

    [Fact]
    public void Cleanup_RemovesOnlyOldSessions()
    {
        var old = Path.Combine(_dir, "sessao-20200101-000000.csv");
        var fresh = Path.Combine(_dir, "sessao-20990101-000000.csv");
        File.WriteAllText(old, "x"); File.WriteAllText(fresh, "x");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-40));
        Assert.Equal(1, HistoryLogger.Cleanup(_dir, 30));
        Assert.False(File.Exists(old)); Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void MissingFolder_Empty()
    {
        Assert.Empty(HistoryLogger.ListSessions(Path.Combine(_dir, "nao-existe")));
        Assert.Null(HistoryLogger.FindUnclean(Path.Combine(_dir, "nao-existe")));
    }
}
