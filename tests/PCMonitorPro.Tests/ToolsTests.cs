using Xunit;

namespace PCMonitorPro.Tests;

public class CommandSuggestionsTests
{
    [Fact]
    public void AllCommands_HaveContent_AndKnownKind()
    {
        Assert.Equal(46, CommandSuggestions.All.Count);
        foreach (var c in CommandSuggestions.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Title)); Assert.False(string.IsNullOrWhiteSpace(c.Command));
            Assert.Contains(c.Kind, new[] { "leitura", "altera" });
            Assert.NotEmpty(c.Tags);
        }
    }

    [Fact]
    public void ReadOnlyCommands_DoNotContainDestructiveVerbs()
    {
        foreach (var c in CommandSuggestions.All.Where(c => !c.Alters))
            Assert.DoesNotMatch(@"Remove-Item|Set-ItemProperty|Restart-Service|Clear-DnsClientCache|Update-MpSignature|Start-MpScan|/RestoreHealth|sfc /scannow", c.Command);
    }

    [Fact]
    public void Suggest_GroupsLinkedFirst_AndKeepsOrderInsideGroup()
    {
        var items = CommandSuggestions.Suggest(Diagnostics.Scenarios["rede"].Focus, new HashSet<string> { "disk" });
        var groups = items.Select(i => i.Group).ToList();
        Assert.Equal(groups.OrderBy(g => g == CommandSuggestions.GroupLinked ? 0 : g == CommandSuggestions.GroupFocus ? 1 : 2), groups);
        Assert.Contains(items, i => i.Group == CommandSuggestions.GroupLinked && i.Command.Tags.Contains("disk"));
        Assert.Contains(items, i => i.Group == CommandSuggestions.GroupFocus && i.Command.Tags.Contains("net"));
    }

    [Fact]
    public void Suggest_NoFocus_ReturnsEverything_AndShowAllUsesSingleGroup()
    {
        Assert.Equal(46, CommandSuggestions.Suggest(null).Count);
        Assert.All(CommandSuggestions.Suggest(Diagnostics.Scenarios["rede"].Focus, null, showAll: true),
            i => Assert.Equal(CommandSuggestions.GroupAll, i.Group));
    }

    [Fact]
    public void ReportText_EndsWithCommands_AndCanOmitThem()
    {
        var rep = DiagnosticsAnalyzer.Analyze(Sample.Bad(), now: Sample.Now);
        Assert.Contains("COMANDOS SUGERIDOS DO POWERSHELL", rep.ToText());
        Assert.DoesNotContain("COMANDOS SUGERIDOS DO POWERSHELL", rep.ToText(includeCommands: false));
        Assert.True(CommandSuggestions.ForReport(rep).Split('\n').Count(l => l.StartsWith("  # ")) <= 12);
    }
}

[Collection("pasta-de-dados")]
public class ReportStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pmp-rep-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Save_NeverOverwrites()
    {
        var now = new DateTime(2026, 1, 2, 3, 4, 5);
        var a = ReportStore.Save("um", now, _dir);
        var b = ReportStore.Save("dois", now, _dir);
        Assert.NotEqual(a, b);
        Assert.Equal("um", File.ReadAllText(a));
        Assert.Equal("dois", File.ReadAllText(b));
        Assert.EndsWith("diagnostico-20260102-030405.txt", a);
    }
}

public class CommandRunnerTests
{
    [Fact]
    public async Task Run_CapturesOutput_AndExitCode()
    {
        var sb = new System.Text.StringBuilder();
        int code = await CommandRunner.RunAsync("Write-Output 'ola mundo'; Write-Error 'falha de teste' -ErrorAction Continue", s => { lock (sb) sb.Append(s); });
        Assert.Contains("ola mundo", sb.ToString());
        Assert.Contains("falha de teste", sb.ToString());
        Assert.Equal(0, code);
        Assert.False(CommandRunner.Running);
    }

    [Fact]
    public async Task Run_CanBeCancelled()
    {
        using var cts = new CancellationTokenSource();
        var task = CommandRunner.RunAsync("Start-Sleep -Seconds 30", _ => { }, cts.Token);
        await Task.Delay(1500);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(CommandRunner.Running);
    }
}

[Collection("pasta-de-dados")]
public class SettingsMilestoneTests : IDisposable
{
    private readonly string _old = AppSettings.Folder;
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "pmp-ms-" + Guid.NewGuid().ToString("N"));
    public SettingsMilestoneTests() { Directory.CreateDirectory(_tmp); AppSettings.Folder = _tmp; AppLog.UsePath(Path.Combine(_tmp, "t.log")); }
    public void Dispose() { AppSettings.Folder = _old; AppLog.UsePath(null); try { Directory.Delete(_tmp, true); } catch { } }

    [Fact]
    public void Milestone_RoundTrips()
    {
        var s = AppSettings.Load();
        s.MilestoneTime = new DateTime(2026, 11, 18, 10, 0, 0); s.MilestoneNote = "EXPO desativado"; s.DiagScenario = "quedas";
        s.Save();
        var l = AppSettings.Load();
        Assert.Equal(new DateTime(2026, 11, 18, 10, 0, 0), l.MilestoneTime);
        Assert.Equal("EXPO desativado", l.MilestoneNote);
        Assert.Equal("quedas", l.DiagScenario);
    }

    [Fact]
    public void SessionRecorder_FindsPreviousCrash_AndStartsNewSession()
    {
        var folder = Path.Combine(_tmp, "historico");
        var crashed = new HistoryLogger(folder);   // sessão antiga com dados e sem Close(): simula queda
        crashed.Start(HistorySampler.Catalog, DateTime.Now.AddMinutes(-5));
        crashed.Write(new Dictionary<string, object?> { ["cpu.temp"] = 97.0 }, DateTime.Now.AddMinutes(-4));
        var rec = new SessionRecorder(folder); rec.Start();
        Assert.NotNull(rec.PreviousUnclean);
        Assert.Equal(crashed.Path, rec.PreviousUnclean!.Path);
        Assert.NotEqual(crashed.Path, rec.CurrentPath);
        rec.Dispose(); crashed.Dispose();
    }
}
