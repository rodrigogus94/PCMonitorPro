using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace PCMonitorPro.Tests;

/// <summary>Dados sintéticos (sem informação pessoal) para testar DiagnosticsAnalyzer.Analyze().</summary>
internal static class Sample
{
    public static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0);

    public static string T(int daysAgo, int hour = 3) =>
        Now.AddDays(-daysAgo).Date.AddHours(hour).ToString("s");

    private static JsonObject O(object o) => JsonSerializer.SerializeToNode(o)!.AsObject();

    public static JsonObject Bad()
    {
        long[] codes = { 0x1A, 0x50, 0x3B, 0x0A, 0x139, 0x1E };
        var apps = new (string a, string m, string c)[]
        {
            ("dwm.exe", "ntdll.dll", "c0000005"), ("MsMpEng.exe", "x.dll", "c0000374"), ("chrome.exe", "nvlddmkm.sys", "c0000005"),
            ("game.exe", "nvwgf2um.dll", "c0000374"), ("svchost.exe", "a.dll", "c0000374"), ("steam.exe", "b.dll", "c0000005"),
            ("code.exe", "c.dll", "c0000005"), ("explorer.exe", "d.dll", "c0000409"), ("a.exe", "nvd3dum.dll", "c0000005"),
            ("b.exe", "e.dll", "c0000005"),
        };
        return O(new
        {
            os = new { caption = "Microsoft Windows 11 Pro", build = "26300", version = "10.0.26300", display = "25H2", ubr = 100,
                       boot = T(20), branch = "rs_prerelease", ring = "Dev", content = "Mainline", flight = "True",
                       ramKb = 16 * 1048576, ramFreeKb = 1 * 1048576 },
            board = new { maker = "ASUS", model = "System Product", board = "PRIME", biosVersion = "1001", biosDate = T(500),
                          cpu = "AMD Ryzen 7 7800X3D", cores = 8, threads = 16 },
            ram = new[]
            {
                new { slot = "A1", gb = 16, rated = 6000, configured = 6000, maker = "X", part = "P1", type = 34 },
                new { slot = "B1", gb = 16, rated = 6000, configured = 6000, maker = "X", part = "P2", type = 34 },
            },
            disks = new[] { new { name = "SSD1", media = "SSD", bus = "NVMe", health = "Warning", op = "OK", gb = 500, temp = 70, wear = 85, readErr = 3, writeErr = 0, hours = 100 } },
            volumes = new[] { new { letter = "C", label = "", fs = "NTFS", gb = 500, freeGb = 10 } },
            gpus = new[] { new { name = "NVIDIA RTX", driver = "1.0", date = T(900) }, new { name = "AMD Radeon(TM) Graphics", driver = "2.0", date = T(30) } },
            pnp = new[] { new { cls = "Net", name = "Adaptador", status = "Error", problem = "CM_PROB_FAILED" } },
            power = new { fastStartup = 1, pendingReboot = true, plan = "Equilibrado" },
            hotfix = new[] { new { id = "KB1", on = T(200) } },
            minidumps = Array.Empty<object>(),
            ev_power = new[] { 0, 1, 2, 3, 5, 8, 12, 20 }.Select(d => new { t = T(d), id = 41, p = "Kernel-Power", m = "" }).ToArray(),
            ev_bugcheck = codes.Select((c, i) => new { t = T(i, 3), id = 1001, p = "WER", m = $"verificação de bugs 0x{c:X8}" }).ToArray(),
            ev_whea = new[] { new { t = T(1), id = 18, p = "WHEA-Logger", m = "Erro de hardware fatal" } },
            ev_disk = new[] { new { t = T(2), id = 153, p = "disk", m = "x" }, new { t = T(3), id = 7, p = "disk", m = "y" } },
            ev_dump = new[] { new { t = T(1), id = 161, p = "volmgr", m = "" } },
            ev_memdiag = new[] { new { t = T(10), id = 1201, p = "MemoryDiagnostics-Results", m = "memória defeituosa" } },
            ev_gpu = Enumerable.Repeat(new { t = T(2), id = 4101, p = "Display", m = "driver timeout" }, 3).ToArray(),
            ev_exhaust = new[] { new { t = T(2), id = 2004, p = "Resource", m = "" } },
            ev_apps = apps.Select((x, i) => new { t = T(i % 5), app = x.a, module = x.m, code = x.c }).ToArray(),
            ev_hangs = Enumerable.Repeat(new { t = T(1), app = "chrome.exe" }, 11).ToArray(),
            ev_update = new[] { 1, 2, 3 }.Select(i => new { t = T(i), id = 20, p = "WindowsUpdateClient", m = "falha" }).ToArray(),
            startup = Enumerable.Range(0, 21).Select(i => new { name = $"App{i}", loc = "HKLM", cmd = "x" }).ToArray(),
            perf = new
            {
                cpuAvg = 91.0, diskQueue = 3.0, diskLatMs = 40.0, diskBusy = 95.0, commit = 93.0,
                topCpu = new[] { new { name = "miner", cpu = 60.0, ramMb = 500, n = 1 } },
                topRam = new[] { new { name = "chrome", cpu = 5.0, ramMb = 4000, n = 20 } },
            },
            net = new
            {
                adapters = new[]
                {
                    new { name = "Ethernet", desc = "x", status = "Up", speed = "100 Mbps", media = "802.3" },
                    new { name = "Wi-Fi", desc = "y", status = "Up", speed = "300 Mbps", media = "Native 802.11" },
                },
                gateway = "192.168.0.1", dns = new[] { "1.1.1.1" }, wifiSignal = 30, dnsOk = false, dnsMs = 900,
                pingGw = new { addr = "192.168.0.1", sent = 5, ok = 3, avg = 50.0, max = 90 },
                pingNet1 = new { addr = "1.1.1.1", sent = 5, ok = 5, avg = 20.0, max = 30 },
                pingNet2 = new { addr = "8.8.8.8", sent = 5, ok = 5, avg = 22.0, max = 30 },
            },
            update = new { wuStatus = "Stopped", wuStart = "Disabled" },
            security = new { av = "False", rtp = "False", sigAge = 30, quickAge = 40, fw = new[] { new { name = "Public", on = "False" } } },
            threats = new[] { new { t = T(3), id = "123" } },
            errors = new[] { "exemplo: aviso de coleta" },
            collected = Now.ToString("s"),
        });
    }

    public static JsonObject Healthy() => O(new
    {
        os = new { caption = "Microsoft Windows 11 Home", build = "26100", display = "24H2", ubr = 1, boot = T(1), branch = "", ring = "", flight = "",
                   ramKb = 32 * 1048576, ramFreeKb = 20 * 1048576 },
        board = new { maker = "Dell", model = "XPS", board = "0X", biosVersion = "2.0", biosDate = T(100), cpu = "Intel Core i7", cores = 8, threads = 16 },
        ram = Enumerable.Repeat(new { slot = "A1", gb = 16, rated = 3200, configured = 3200, maker = "K", part = "P", type = 26 }, 2).ToArray(),
        disks = new[] { new { name = "SSD", media = "SSD", bus = "NVMe", health = "Healthy", op = "OK", gb = 500, temp = 40, wear = 5, readErr = 0, writeErr = 0, hours = 10 } },
        volumes = new[] { new { letter = "C", label = "", fs = "NTFS", gb = 500, freeGb = 300 } },
        gpus = new[] { new { name = "NVIDIA RTX", driver = "1.0", date = T(30) } },
        pnp = Array.Empty<object>(), power = new { fastStartup = 0, pendingReboot = false, plan = "x" },
        hotfix = new[] { new { id = "KB1", on = T(10) } },
        ev_power = Array.Empty<object>(), ev_bugcheck = Array.Empty<object>(), ev_whea = Array.Empty<object>(),
        ev_disk = Array.Empty<object>(), ev_dump = Array.Empty<object>(),
        ev_memdiag = new[] { new { t = T(5), id = 1101, p = "x", m = "sem erros" } },
        ev_gpu = Array.Empty<object>(), ev_exhaust = Array.Empty<object>(), ev_apps = Array.Empty<object>(),
        ev_hangs = Array.Empty<object>(), ev_update = Array.Empty<object>(),
        startup = new[] { new { name = "A", loc = "x", cmd = "y" } },
        perf = new { cpuAvg = 10.0, diskQueue = 0.1, diskLatMs = 2.0, diskBusy = 5.0, commit = 30.0, topCpu = Array.Empty<object>(),
                     topRam = new[] { new { name = "chrome", cpu = 1, ramMb = 800, n = 3 } } },
        net = new
        {
            adapters = new[] { new { name = "Wi-Fi", desc = "y", status = "Up", speed = "866 Mbps", media = "Native 802.11" } },
            gateway = "192.168.0.1", dns = new[] { "1.1.1.1" }, wifiSignal = 80, dnsOk = true, dnsMs = 20,
            pingGw = new { addr = "g", sent = 5, ok = 5, avg = 2.0, max = 3 },
            pingNet1 = new { addr = "a", sent = 5, ok = 5, avg = 15.0, max = 20 },
            pingNet2 = new { addr = "b", sent = 5, ok = 5, avg = 16.0, max = 20 },
        },
        update = new { wuStatus = "Running", wuStart = "Manual" },
        security = new { av = "True", rtp = "True", sigAge = 1, quickAge = 2, fw = new[] { new { name = "Public", on = "True" } } },
        threats = Array.Empty<object>(), errors = Array.Empty<object>(), collected = Now.ToString("s"),
    });

    public static JsonObject NetDown()
    {
        var d = Healthy();
        d["net"] = JsonNode.Parse("""
        {"adapters":[{"name":"Ethernet","desc":"x","status":"Up","speed":"1 Gbps","media":"802.3"}],
         "gateway":"192.168.0.1","dns":[],"wifiSignal":null,"dnsOk":false,"dnsMs":5000,
         "pingGw":{"addr":"g","sent":5,"ok":5,"avg":2.0,"max":3},
         "pingNet1":{"addr":"a","sent":5,"ok":0,"avg":null,"max":null},
         "pingNet2":{"addr":"b","sent":5,"ok":0,"avg":null,"max":null}}
        """);
        return d;
    }

    public static readonly Dictionary<string, Func<JsonNode>> Fixtures = new()
    {
        ["bad"] = Bad, ["healthy"] = Healthy, ["net_down"] = NetDown, ["empty"] = () => new JsonObject(),
    };

    public static List<LastValues> HistoryInfo() => new()
    {
        new("2026-11-19 10:00:00", new() { ["cpu.temp"] = 93.0, ["nv0.temp"] = 60.0, ["ram.pct"] = 90.0 }, Now),
    };

    public static RecentStats Recent(double hours = 0) =>
        new(720, hours, new() { ["cpu.temp"] = (96.0, 60.0), ["nv0.temp"] = (70.0, 50.0) });
}

public class DiagnosticsTests
{
    private static DiagnosticReport Run(JsonNode raw, string scenario = "geral", IReadOnlyList<LastValues>? hist = null,
        Milestone? ms = null, RecentStats? recent = null) =>
        DiagnosticsAnalyzer.Analyze(raw, hist, ms, Sample.Now, scenario, recent);

    private static List<string> Titles(DiagnosticReport r) => r.Findings.Select(f => f.Title).ToList();

    [Fact]
    public void AllFixturesAndScenariosRun()
    {
        foreach (var (name, make) in Sample.Fixtures)
            foreach (var scn in Diagnostics.Scenarios.Keys)
            {
                var rep = Run(make(), scn, Sample.HistoryInfo(), new Milestone(Sample.Now.AddDays(-3), "EXPO desativado"), Sample.Recent());
                Assert.False(string.IsNullOrWhiteSpace(rep.ToText()), $"{name}/{scn}");
                Assert.Equal(rep.Findings.Count, rep.Relevant.Count + rep.Others.Count);
            }
    }

    [Fact]
    public void HealthyPc_HasNoProblem()
    {
        var rep = Run(Sample.Healthy());
        Assert.Null(rep.Top);
        Assert.DoesNotContain(rep.Findings, f => f.Sev is "crit" or "warn");
    }

    [Fact]
    public void BadPc_PointsToRam_WithHighConfidence()
    {
        var rep = Run(Sample.Bad());
        Assert.Equal("ram", rep.Top!.Key);
        Assert.Equal("alta", rep.Confidence);
        Assert.NotEmpty(rep.Top.Evidence); Assert.NotEmpty(rep.Top.Advice);
    }

    [Fact]
    public void HypothesesAndFindings_AreSorted()
    {
        var rep = Run(Sample.Bad());
        var scores = rep.Hypotheses.Select(h => h.Score).ToList();
        Assert.Equal(scores.OrderByDescending(x => x), scores);
        var sev = rep.Findings.Select(f => Diagnostics.SevOrder[f.Sev]).ToList();
        Assert.Equal(sev.OrderBy(x => x), sev);
    }

    [Fact]
    public void ScenarioFocus_SplitsFindings()
    {
        var rep = Run(Sample.Bad(), "rede");
        Assert.All(rep.Relevant, f => Assert.Contains("net", f.Tags));
        Assert.NotEmpty(rep.Others);
    }

    [Fact]
    public void DnsFailure_IsTopForNetworkScenario()
    {
        var rep = Run(Sample.NetDown(), "rede");
        Assert.Contains(rep.Top!.Key, new[] { "netdns", "netisp" });
    }

    [Fact]
    public void EmptyInput_DoesNotCrash() => Assert.Null(Run(new JsonObject()).Top);

    [Fact]
    public void NullInput_DoesNotCrash() => Assert.Null(DiagnosticsAnalyzer.Analyze(null, now: Sample.Now).Top);

    [Fact]
    public void UnknownScenario_FallsBackToGeneral() => Assert.Equal("geral", Run(Sample.Healthy(), "nao-existe").Scenario);

    [Fact]
    public void SharedStateBetweenSections()
    {
        // espaço do C: vem da seção de discos e é lido pela de Windows Update
        Assert.Contains(Titles(Run(Sample.Bad(), "update")), t => t.Contains("Menos de 15 GB"));
        // a seção de vídeo lê os códigos de tela azul da seção de telas azuis
        var raw = Sample.Healthy();
        raw["ev_bugcheck"] = JsonNode.Parse("""[{"t":"2026-11-19T10:00:00","id":1001,"p":"x","m":"0x116"},{"t":"2026-11-18T10:00:00","id":1001,"p":"x","m":"0x117"}]""");
        raw["ev_power"] = JsonNode.Parse("""[{"t":"2026-11-19T10:00:05","id":41,"p":"x","m":""},{"t":"2026-11-18T10:00:05","id":41,"p":"x","m":""}]""");
        Assert.Contains("gpu", Run(raw).Hypotheses.Select(h => h.Key));
    }

    private static DiagnosticReport Whea(params int[] ids)
    {
        var raw = Sample.Healthy();
        raw["ev_whea"] = new JsonArray(ids.Select(i => JsonNode.Parse("{\"t\":\"2026-11-19T10:00:00\",\"id\":" + i + ",\"p\":\"WHEA-Logger\",\"m\":\"evento\"}")).ToArray());
        return Run(raw);
    }

    [Fact]
    public void CorrectedWheaErrors_AreNotCritical()
    {
        var rep = Whea(19, 19, 17);
        Assert.DoesNotContain(rep.Findings, f => f.Sev == "crit");
        Assert.Null(rep.Top);
        Assert.Contains(Titles(rep), t => t.Contains("corrigidos"));
    }

    [Fact]
    public void ManyCorrectedWheaErrors_WarnLightly()
    {
        var rep = Whea(Enumerable.Repeat(19, 25).ToArray());
        Assert.Equal("hw", rep.Top!.Key);
        Assert.True(rep.Top.Score < 10);
    }

    [Fact]
    public void UncorrectedWheaError_IsCritical()
    {
        var rep = Whea(18);
        Assert.Contains(rep.Findings, f => f.Sev == "crit");
        Assert.Equal("hw", rep.Top!.Key);
    }

    [Fact]
    public void NewStableBuild_IsNotFlagged_ButInsiderIs()
    {
        var raw = Sample.Healthy(); raw["os"]!["build"] = "26300";
        Assert.DoesNotContain("os", Run(raw).Hypotheses.Select(h => h.Key));
        raw["os"]!["ring"] = "Dev";
        Assert.Contains("os", Run(raw).Hypotheses.Select(h => h.Key));
    }

    private static Finding Ddr5(string cpu)
    {
        var raw = Sample.Healthy(); raw["board"]!["cpu"] = cpu;
        raw["ram"] = JsonNode.Parse("""[{"slot":"A1","gb":16,"rated":6000,"configured":6000,"maker":"K","part":"P","type":34},{"slot":"A1","gb":16,"rated":6000,"configured":6000,"maker":"K","part":"P","type":34}]""");
        return Run(raw).Findings.First(f => f.Title.Contains("MT/s"));
    }

    [Fact]
    public void Am5Note_OnlyForRyzen7000And9000()
    {
        Assert.Contains("AM5", Ddr5("AMD Ryzen 7 7800X3D").Detail);
        Assert.DoesNotContain("AM5", Ddr5("Intel Core i7-14700K").Detail);
        Assert.DoesNotContain("AM5", Ddr5("AMD Ryzen 5 5600X").Detail);
    }

    [Fact]
    public void RecentHours_UsesRealHours_OrRowsTimesInterval()
    {
        Assert.Contains(Titles(Run(Sample.Healthy(), recent: Sample.Recent(2.0))), t => t.Contains("últimas ~2 h"));
        Assert.Contains(Titles(Run(Sample.Healthy(), recent: Sample.Recent())), t => t.Contains("últimas ~1 h"));   // 720 x 5 s
    }

    [Fact]
    public void Milestone_Compares_BeforeAfter()
    {
        var rep = Run(Sample.Bad(), ms: new Milestone(Sample.Now.AddDays(-3), "EXPO desativado"));
        Assert.Contains(Titles(rep), t => t.Contains("EXPO desativado"));
    }

    [Fact]
    public void HotCrash_History_AddsThermalHypothesis()
    {
        var rep = Run(Sample.Healthy(), hist: Sample.HistoryInfo());
        Assert.Contains("thermal", rep.Hypotheses.Select(h => h.Key));
    }

    [Fact]
    public void MissingSecurityFields_AreNotReportedAsDisabled()
    {
        var raw = Sample.Healthy();
        raw["security"] = JsonNode.Parse("""{"av":null,"rtp":null,"sigAge":null,"fw":[]}""");
        Assert.DoesNotContain(Titles(Run(raw)), t => t.Contains("pendências"));
    }

    [Theory]
    [InlineData("866 Mbps", 866.0)]
    [InlineData("1 Gbps", 1000.0)]
    [InlineData("1,5 Gbps", 1500.0)]
    [InlineData("100 Kbps", 0.1)]
    public void ParseMbps_Units(string s, double expected) => Assert.Equal(expected, DiagnosticsAnalyzer.ParseMbps(s)!.Value, 6);

    [Fact]
    public void ParseMbps_Invalid_IsNull() => Assert.Null(DiagnosticsAnalyzer.ParseMbps("sem link"));

    [Fact]
    public void CollectScript_IsAscii_ForStdin() =>
        Assert.True(DiagnosticsCollector.Script.All(c => c < 128), "o script vai por stdin em ASCII");

    [Fact]
    public void ParseOutput_ExtractsJson_AndRejectsGarbage()
    {
        var n = DiagnosticsCollector.ParseOutput("lixo antes {\"a\":1} lixo depois");
        Assert.Equal(1, (int)n["a"]!);
        Assert.Throws<InvalidOperationException>(() => DiagnosticsCollector.ParseOutput("nada", "erro x"));
    }

    [Fact]
    public void Cancel_WithoutRunningProcess_IsSafe() => DiagnosticsCollector.Cancel();

    /// <summary>Teste ao vivo (lento, lê o PC real): só roda com a variável PMP_LIVE=1. PMP_LIVE_OUT grava o relatório.</summary>
    [Fact]
    public async Task Live_CollectAndAnalyze_ThisMachine()
    {
        if (Environment.GetEnvironmentVariable("PMP_LIVE") != "1") return;
        var raw = await DiagnosticsCollector.CollectAsync();
        var rep = DiagnosticsAnalyzer.Analyze(raw);
        var text = rep.ToText();
        Assert.Contains("SISTEMA", text);
        Assert.NotEmpty(rep.System);
        var outPath = Environment.GetEnvironmentVariable("PMP_LIVE_OUT");
        if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, text);
    }
}
