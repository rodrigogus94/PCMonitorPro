using Xunit;

namespace PCMonitorPro.Tests;

public class AlertTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);
    private static AppSettings Cfg() => new() { AlertsEnabled = true, AlertTempC = 90, AlertRamPct = 95, AlertCooldownS = 300 };
    private static TempReading[] Temps(float cpu, float gpu = 50) => new[] { new TempReading("cpu", "CPU", cpu), new TempReading("gpu", "GPU", gpu) };

    [Fact]
    public void Below_limits_gives_nothing()
        => Assert.Empty(new AlertMonitor().Evaluate(Cfg(), Temps(70), 60f, T0));

    [Fact]
    public void Temp_and_ram_over_limit_alert_once_each()
    {
        var lines = new AlertMonitor().Evaluate(Cfg(), Temps(92), 97f, T0);
        Assert.Equal(2, lines.Count);
        Assert.Contains("CPU em 92 °C", lines[0]);
        Assert.Contains("RAM em 97%", lines[1]);
    }

    [Fact]
    public void Limit_is_inclusive()
        => Assert.Single(new AlertMonitor().Evaluate(Cfg(), Temps(90), 94f, T0));

    [Fact]
    public void Cooldown_blocks_repeat_then_releases()
    {
        var m = new AlertMonitor();
        Assert.Single(m.Evaluate(Cfg(), Temps(95), null, T0));
        Assert.Empty(m.Evaluate(Cfg(), Temps(95), null, T0.AddSeconds(299)));
        Assert.Single(m.Evaluate(Cfg(), Temps(95), null, T0.AddSeconds(300)));
    }

    [Fact]
    public void Cooldown_is_per_sensor()
    {
        var m = new AlertMonitor();
        Assert.Single(m.Evaluate(Cfg(), Temps(95), null, T0));
        var lines = m.Evaluate(Cfg(), Temps(95, 99), null, T0.AddSeconds(5));   // CPU em cooldown, GPU é novo
        Assert.Single(lines);
        Assert.Contains("GPU", lines[0]);
    }

    [Fact]
    public void Garbage_sensor_above_125_is_ignored()
        => Assert.Empty(new AlertMonitor().Evaluate(Cfg(), Temps(255), null, T0));

    [Fact]
    public void Disabled_gives_nothing()
    {
        var c = Cfg(); c.AlertsEnabled = false;
        Assert.Empty(new AlertMonitor().Evaluate(c, Temps(110), 99f, T0));
    }

    [Fact]
    public void Missing_ram_reading_is_fine()
        => Assert.Empty(new AlertMonitor().Evaluate(Cfg(), Temps(60), null, T0));

    [Fact]
    public void Format_limits_to_four_lines_and_counts_rest()
    {
        var text = AlertMonitor.Format(Enumerable.Range(1, 6).Select(i => "l" + i).ToList());
        Assert.Equal("l1\nl2\nl3\nl4\n(+2 outros)", text);
        Assert.Equal("a\nb", AlertMonitor.Format(new[] { "a", "b" }));
    }

    [Fact]
    public void Settings_roundtrip_keeps_alert_values()
    {
        var c = new AppSettings { AlertTempC = 80, AlertRamPct = 90, AlertsEnabled = false };
        var json = System.Text.Json.JsonSerializer.Serialize(c);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal((80, 90, false), (back.AlertTempC, back.AlertRamPct, back.AlertsEnabled));
    }
}
