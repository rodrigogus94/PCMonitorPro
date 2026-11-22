using Xunit;

namespace PCMonitorPro.Tests;

public class ProfileTests
{
    private static readonly ProfileSection[] All = Enum.GetValues<ProfileSection>();

    private static AppSettings Rich()
    {
        var s = new AppSettings
        {
            LightTheme = true, AlertTempC = 85, AlertRamPct = 90, AlertCooldownS = 120, HistoryEnabled = false, HistoryKeepDays = 30,
            CloseToTray = true, DiagScenario = "disco", OverlayCorner = "BR", OverlayScale = 1.3, OverlayItems = new() { "fps", "gpu_load" },
            WidgetTempWarn = 70, WidgetTempBad = 85,
        };
        s.Widgets.Add(new WidgetConfig { Name = "Jogo", Layout = "horizontal", Accent = "#112233", X = 300, Y = 200, Metrics = new() { "cpu.load", "s:/amdcpu/0/temperature/2" } });
        s.BenchRuns.Add(new BenchRun(new DateTime(2026, 5, 1), 12000, 1900, true, "Ryzen 5 5600"));
        s.BenchRecords.Add(new BenchRecord(new DateTime(2026, 5, 2), "gpu", 7000, "7000 pts"));
        // dados que NUNCA podem sair no arquivo
        s.HowManyFpsKey = "CHAVE-SECRETA-123";
        s.TweakBackup["Chave"] = "valor-do-backup";
        s.StartupRemoved.Add(new StartupBackup { Name = "ProgramaRemovido", Command = @"C:\x\remover.exe" });
        s.ComDbBackup = new byte[] { 1, 2, 3 };
        s.GameProfiles["g1"] = new GameProfile { ExePath = @"C:\Jogos\jogo.exe", Helpers = { new HelperProgram { Path = @"C:\Tools\malvado.exe" } } };
        s.CustomGames.Add(new CustomGame { Name = "Meu jogo", Exe = @"D:\Jogos\meu.exe" });
        s.ReportFolder = @"D:\Relatorios";
        s.MilestoneNote = "mudei o EXPO";
        return s;
    }

    // ---------- o que sai e o que não sai ----------

    [Fact]
    public void Export_never_contains_secrets_backups_or_launch_data()
    {
        var json = ProfileTransfer.Export(Rich(), All, includeComputerName: true);
        foreach (var forbidden in new[] { "CHAVE-SECRETA", "HowManyFps", "TweakBackup", "valor-do-backup", "StartupRemoved", "ProgramaRemovido", "ComDb",
                     "GameProfiles", "jogo.exe", "malvado.exe", "CustomGames", "meu.exe", "Relatorios", "ReportFolder", "mudei o EXPO", "Milestone" })
            Assert.DoesNotContain(forbidden, json);
    }

    [Fact]
    public void Export_only_chosen_sections()
    {
        var f = ProfileTransfer.Read(ProfileTransfer.Export(Rich(), new[] { ProfileSection.Alerts, ProfileSection.Tray }, false));
        Assert.Equal(new[] { ProfileSection.Alerts, ProfileSection.Tray }, ProfileTransfer.Available(f));
        Assert.Null(f.Widgets);
        Assert.Null(f.Computer);
    }

    [Fact]
    public void Computer_name_is_optional()
    {
        Assert.Equal(Environment.MachineName, ProfileTransfer.Read(ProfileTransfer.Export(Rich(), All, true)).Computer);
        Assert.Null(ProfileTransfer.Read(ProfileTransfer.Export(Rich(), All, false)).Computer);
    }

    // ---------- ida e volta ----------

    [Fact]
    public void Roundtrip_restores_everything_exported()
    {
        var src = Rich();
        var dst = new AppSettings();
        ProfileTransfer.Apply(dst, ProfileTransfer.Read(ProfileTransfer.Export(src, All, false)), All);

        Assert.True(dst.LightTheme);
        Assert.Equal((85, 90, 120), (dst.AlertTempC, dst.AlertRamPct, dst.AlertCooldownS));
        Assert.False(dst.HistoryEnabled);
        Assert.Equal(30, dst.HistoryKeepDays);
        Assert.True(dst.CloseToTray);
        Assert.Equal("disco", dst.DiagScenario);
        Assert.Equal(("BR", 1.3), (dst.OverlayCorner, dst.OverlayScale));
        Assert.Equal(new[] { "fps", "gpu_load" }, dst.OverlayItems);
        Assert.Equal((70, 85), (dst.WidgetTempWarn, dst.WidgetTempBad));
        var w = Assert.Single(dst.Widgets);
        Assert.Equal(("Jogo", "horizontal", "#112233", 300, 200), (w.Name, w.Layout, w.Accent, w.X, w.Y));
        Assert.Equal(new[] { "cpu.load", "s:/amdcpu/0/temperature/2" }, w.Metrics);
        Assert.Single(dst.BenchRuns);
        Assert.Single(dst.BenchRecords);
    }

    [Fact]
    public void Apply_gives_widgets_new_ids_and_leaves_other_settings_alone()
    {
        var src = Rich();
        var dst = new AppSettings();
        dst.HowManyFpsKey = "minha-chave";
        dst.GameProfiles["x"] = new GameProfile { ExePath = @"C:\meu\jogo.exe" };
        dst.ReportFolder = @"E:\R";
        ProfileTransfer.Apply(dst, ProfileTransfer.Read(ProfileTransfer.Export(src, All, false)), All);
        Assert.NotEqual(src.Widgets[0].Id, dst.Widgets[0].Id);
        Assert.Equal("minha-chave", dst.HowManyFpsKey);
        Assert.Contains("x", dst.GameProfiles.Keys);
        Assert.DoesNotContain("g1", dst.GameProfiles.Keys);
        Assert.Equal(@"E:\R", dst.ReportFolder);
    }

    [Fact]
    public void Apply_ignores_sections_not_chosen_or_not_in_file()
    {
        var dst = new AppSettings { AlertTempC = 77 };
        var f = ProfileTransfer.Read(ProfileTransfer.Export(Rich(), new[] { ProfileSection.Alerts }, false));
        ProfileTransfer.Apply(dst, f, new[] { ProfileSection.Widgets });            // escolheu algo que o arquivo não tem
        Assert.Equal(77, dst.AlertTempC);
        Assert.Empty(dst.Widgets);
        ProfileTransfer.Apply(dst, f, new[] { ProfileSection.Alerts });
        Assert.Equal(85, dst.AlertTempC);
    }

    // ---------- arquivo hostil ----------

    [Fact]
    public void Apply_clamps_and_cleans_hostile_values()
    {
        var f = new ProfileFile
        {
            Widgets = new()
            {
                TempWarn = 90, TempBad = 10,   // invertido: ignora
                Items = Enumerable.Range(0, 50).Select(i => new WidgetConfig
                {
                    Name = new string('N', 500), Opacity = 99, Scale = -5, X = int.MaxValue, Y = int.MinValue, Accent = "<script>",
                    Metrics = Enumerable.Range(0, 5000).Select(m => "m" + m).ToList(),
                }).ToList(),
            },
            Alerts = new() { TempC = 5, RamPct = 5000, CooldownS = -1 },
            History = new() { KeepDays = 99999 },
            Overlay = new() { Corner = "XX", Scale = 0.9, Items = new() { "fps", "nao_existe", "fps", "../../etc" } },
            Diagnostic = new() { Scenario = "inexistente" },
        };
        var t = new AppSettings();
        ProfileTransfer.Apply(t, f, All);

        Assert.Equal(20, t.Widgets.Count);
        var w = t.Widgets[0];
        Assert.Equal(30, w.Name.Length);
        Assert.Equal(40, w.Metrics.Count);
        Assert.Equal((1.0, 0.6), (w.Opacity, w.Scale));
        Assert.Equal((20000, -20000), (w.X, w.Y));
        Assert.Equal("#4cc9f0", w.Accent);
        Assert.Equal(20, t.Widgets.Select(x => x.Id).Distinct().Count());
        Assert.Equal((75, 88), (t.WidgetTempWarn, t.WidgetTempBad));   // par invertido não entra
        Assert.Equal((50, 100, 10), (t.AlertTempC, t.AlertRamPct, t.AlertCooldownS));
        Assert.Equal(365, t.HistoryKeepDays);
        Assert.Equal(("TL", 0.85), (t.OverlayCorner, t.OverlayScale));
        Assert.Equal(new[] { "fps" }, t.OverlayItems);
        Assert.Equal("geral", t.DiagScenario);
    }

    [Fact]
    public void Benchmarks_are_merged_without_duplicates_and_capped()
    {
        var f = new ProfileFile
        {
            Benchmarks = new()
            {
                Runs = Enumerable.Range(1, 30).Select(i => new BenchRun(new DateTime(2026, 1, 1).AddDays(i), 100 + i, 50, false, "cpu")).ToList(),
                Records = Enumerable.Range(1, 30).Select(i => new BenchRecord(new DateTime(2026, 1, 1).AddDays(i), "gpu", i, "s"))
                    .Append(new BenchRecord(DateTime.Now, "mem", double.NaN, "invalido")).ToList(),
            },
        };
        var t = new AppSettings();
        t.BenchRuns.Add(new BenchRun(new DateTime(2025, 1, 1), 1, 1, false, "antigo"));
        ProfileTransfer.Apply(t, f, new[] { ProfileSection.Benchmarks });
        ProfileTransfer.Apply(t, f, new[] { ProfileSection.Benchmarks });   // importar duas vezes não duplica

        Assert.Equal(10, t.BenchRuns.Count);
        Assert.Equal(t.BenchRuns.Count, t.BenchRuns.Select(r => r.Date).Distinct().Count());
        Assert.True(t.BenchRuns.SequenceEqual(t.BenchRuns.OrderByDescending(r => r.Date)));
        Assert.Equal(10, t.BenchRecords.Count(r => r.Test == "gpu"));
        Assert.DoesNotContain(t.BenchRecords, r => r.Test == "mem");   // NaN descartado
    }

    // ---------- leitura ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("isto não é json")]
    [InlineData("[1,2,3]")]
    [InlineData("{}")]
    [InlineData("{\"Format\":\"Outro\",\"Version\":1}")]
    [InlineData("{\"Format\":\"PCMonitorPro.Perfil\",\"Version\":99}")]
    [InlineData("{\"Format\":\"PCMonitorPro.Perfil\",\"Version\":0}")]
    [InlineData("{\"Format\":\"PCMonitorPro.Perfil\",\"Version\":1,\"Alerts\":\"texto\"}")]
    [InlineData("{\"Format\":\"PCMonitorPro.Perfil\",\"Version\":1,\"Widgets\":{\"Items\":[{\"X\":\"abc\"}]}}")]
    public void Read_rejects_bad_files_with_a_friendly_error(string json)
        => Assert.Throws<ProfileException>(() => ProfileTransfer.Read(json));

    [Fact]
    public void Read_rejects_oversized_file()
        => Assert.Throws<ProfileException>(() => ProfileTransfer.Read("{\"Format\":\"PCMonitorPro.Perfil\",\"Version\":1,\"App\":\"" + new string('a', (int)ProfileTransfer.MaxBytes + 1) + "\"}"));

    [Fact]
    public void Read_accepts_unknown_extra_fields_for_forward_compatibility()
    {
        var f = ProfileTransfer.Read("{\"Format\":\"PCMonitorPro.Perfil\",\"Version\":1,\"CampoNovo\":123,\"Alerts\":{\"Enabled\":false,\"TempC\":80,\"RamPct\":90,\"CooldownS\":60,\"Extra\":1}}");
        Assert.Equal(new[] { ProfileSection.Alerts }, ProfileTransfer.Available(f));
    }

    [Fact]
    public void Every_section_has_a_display_name()
    {
        foreach (var s in All) Assert.False(string.IsNullOrWhiteSpace(ProfileTransfer.Names[s]));
    }
}
