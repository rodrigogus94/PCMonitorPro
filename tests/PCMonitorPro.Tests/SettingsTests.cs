using Xunit;

namespace PCMonitorPro.Tests;

[Collection("pasta-de-dados")]
public class SettingsTests
{
    [Fact]
    public void Defaults_match_the_original_app()
    {
        var s = new AppSettings();
        Assert.True(s.HistoryEnabled);
        Assert.Equal(14, s.HistoryKeepDays);
        Assert.Null(s.ReportFolder);
        Assert.Equal((75, 88, 80, 95), (s.WidgetTempWarn, s.WidgetTempBad, s.WidgetPctWarn, s.WidgetPctBad));
    }

    [Fact]
    public void Old_settings_file_without_new_fields_loads_with_defaults()
    {
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"LightTheme\":true}")!;
        Assert.True(back.LightTheme);
        Assert.True(back.HistoryEnabled);
        Assert.Equal(14, back.HistoryKeepDays);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(30, 30)]
    [InlineData(9999, 365)]
    public void SessionRecorder_clamps_retention_days(int given, int expected)
    {
        using var rec = new SessionRecorder(Path.Combine(Path.GetTempPath(), "pmp-rec-" + Guid.NewGuid().ToString("N")), given);
        Assert.Equal(expected, rec.KeepDays);
    }

    [Fact]
    public void ReportStore_uses_custom_folder_then_falls_back_to_default()
    {
        var old = ReportStore.Custom;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "pmp-rep-" + Guid.NewGuid().ToString("N"));
            ReportStore.Custom = dir;
            Assert.Equal(dir, ReportStore.Folder);
            ReportStore.Custom = "  ";
            Assert.Equal(ReportStore.DefaultFolder, ReportStore.Folder);
            ReportStore.Custom = null;
            Assert.Equal(ReportStore.DefaultFolder, ReportStore.Folder);
        }
        finally { ReportStore.Custom = old; }
    }
}
