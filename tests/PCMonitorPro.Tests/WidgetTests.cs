using Xunit;

namespace PCMonitorPro.Tests;

public class WidgetTests
{
    private static readonly Dictionary<string, WidgetMetric> Cat = WidgetCatalog.Base.ToDictionary(m => m.Id);

    // ---------- formatação e cores ----------

    [Theory]
    [InlineData("%", 5.04, "5,0%")]
    [InlineData("%", 42.6, "43%")]
    [InlineData("°C", 71.4, "71 °C")]
    [InlineData("GB", 12.34, "12,3 GB")]
    [InlineData("W", 65.5, "65,5 W")]
    [InlineData("MHz", 4650.2, "4650 MHz")]
    [InlineData("RPM", 1200, "1200 RPM")]
    [InlineData("B/s", 2048, "2,0 KB/s")]
    [InlineData("B/s", 5 * 1048576.0, "5,0 MB/s")]
    [InlineData("B/s", 12, "12 B/s")]
    public void FormatValue_by_unit(string unit, double v, string expected)
        => InPtBr(() => Assert.Equal(expected, WidgetFormat.FormatValue(new WidgetMetric("x", "g", "x", "x", unit, null, false), v)));

    /// <summary>A formatação segue a cultura do Windows (vírgula decimal no Brasil); o teste fixa pt-BR para não depender da máquina.</summary>
    private static void InPtBr(Action body)
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try { body(); } finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void FormatValue_without_reading_is_dash()
    {
        Assert.Equal("—", WidgetFormat.FormatValue(Cat["cpu.temp"], null));
        Assert.Equal("—", WidgetFormat.FormatValue(Cat["cpu.temp"], double.NaN));
    }

    [Theory]
    [InlineData("cpu.temp", 60, WidgetLevel.Ok)]
    [InlineData("cpu.temp", 75, WidgetLevel.Warn)]
    [InlineData("cpu.temp", 88, WidgetLevel.Bad)]
    [InlineData("cpu.load", 79, WidgetLevel.Ok)]
    [InlineData("cpu.load", 80, WidgetLevel.Warn)]
    [InlineData("ram.pct", 96, WidgetLevel.Bad)]
    [InlineData("cpu.power", 500, WidgetLevel.Neutral)]   // sem limite: não pinta
    public void Level_follows_original_thresholds(string id, double v, WidgetLevel expected)
        => Assert.Equal(expected, WidgetFormat.LevelFor(Cat[id], v));

    [Fact]
    public void Level_is_neutral_without_value_or_metric()
    {
        Assert.Equal(WidgetLevel.Neutral, WidgetFormat.LevelFor(Cat["cpu.temp"], null));
        Assert.Equal(WidgetLevel.Neutral, WidgetFormat.LevelFor(null, 99));
    }

    [Fact]
    public void BuildRows_uses_config_order_and_survives_unknown_metric()
    {
        var cfg = new WidgetConfig { Metrics = new() { "ram.pct", "nao.existe", "cpu.temp" } };
        var values = new Dictionary<string, float?> { ["ram.pct"] = 50f, ["cpu.temp"] = 90f };
        var rows = WidgetFormat.BuildRows(cfg, Cat, values);
        Assert.Equal(new[] { "RAM", "nao.existe", "CPU temp" }, rows.Select(r => r.Label));
        Assert.Equal(0.5, rows[0].Ratio);
        Assert.Equal("—", rows[1].Text);
        Assert.Null(rows[1].Ratio);
        Assert.Equal(WidgetLevel.Bad, rows[2].Level);
    }

    [Fact]
    public void BuildRows_ratio_is_clamped()
    {
        var cfg = new WidgetConfig { Metrics = new() { "cpu.load" } };
        Assert.Equal(1.0, WidgetFormat.BuildRows(cfg, Cat, new Dictionary<string, float?> { ["cpu.load"] = 250f })[0].Ratio);
    }

    // ---------- configuração ----------

    [Fact]
    public void Sanitize_fixes_out_of_range_values_but_keeps_the_rest()
    {
        var w = new WidgetConfig { Name = "  ", Opacity = 7, Scale = -2, Layout = "diagonal", Accent = "azul", Metrics = new() { "a", "", "a", "b" } };
        w.Sanitize();
        Assert.Equal("Widget", w.Name);
        Assert.Equal(1.0, w.Opacity);
        Assert.Equal(0.6, w.Scale);
        Assert.Equal("vertical", w.Layout);
        Assert.Equal("#4cc9f0", w.Accent);
        Assert.Equal(new[] { "a", "b" }, w.Metrics);
    }

    [Fact]
    public void Sanitize_handles_nan()
    {
        var w = new WidgetConfig { Opacity = double.NaN, Scale = double.PositiveInfinity };
        w.Sanitize();
        Assert.Equal(0.88, w.Opacity);
        Assert.Equal(1.0, w.Scale);
    }

    [Fact]
    public void Clone_does_not_share_metric_list()
    {
        var a = new WidgetConfig();
        var b = a.Clone();
        b.Metrics.Add("x");
        Assert.DoesNotContain("x", a.Metrics);
    }

    [Fact]
    public void Config_roundtrips_through_json_and_old_file_without_widgets_loads()
    {
        var s = new AppSettings();
        s.Widgets.Add(new WidgetConfig { Name = "Jogo", X = 100, Y = 200, ClickThrough = true, Layout = "horizontal", Accent = "#112233" });
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(s))!;
        Assert.Equal((100, 200, true, "horizontal", "#112233"), (back.Widgets[0].X, back.Widgets[0].Y, back.Widgets[0].ClickThrough, back.Widgets[0].Layout, back.Widgets[0].Accent));
        Assert.Empty(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.Widgets);
    }

    [Theory]
    [InlineData("#4cc9f0", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("4cc9f0", false)]
    [InlineData("#fff", false)]
    [InlineData("#gggggg", false)]
    [InlineData(null, false)]
    public void TryParseColor(string? s, bool ok) => Assert.Equal(ok, WidgetFormat.TryParseColor(s, out _));

    // ---------- geometria ----------

    private static readonly PxRect Work = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(10, 300, 0, 300)]        // perto da esquerda
    [InlineData(1710, 300, 1720, 300)]   // 200 de largura: borda direita a 10 px => gruda (x = 1920-200)
    [InlineData(1700, 300, 1700, 300)]   // borda direita a 20 px => não gruda
    [InlineData(500, 8, 500, 0)]         // perto do topo
    [InlineData(500, 835, 500, 840)]     // 200 de altura: base a 5 px => gruda (y = 1040-200)
    [InlineData(500, 500, 500, 500)]     // longe de tudo
    public void Snap_sticks_to_edges_within_margin(int x, int y, int ex, int ey)
        => Assert.Equal((ex, ey), WidgetGeometry.Snap(x, y, 200, 200, Work));

    [Fact]
    public void Snap_respects_work_area_offset_of_second_monitor()
    {
        var second = new PxRect(1920, 0, 3840, 1080);
        Assert.Equal((1920, 100), WidgetGeometry.Snap(1930, 100, 200, 100, second));
    }

    [Fact]
    public void Corner_places_with_margin_in_all_four_corners()
    {
        Assert.Equal((16, 16), WidgetGeometry.Corner(Work, 200, 100, left: true, top: true));
        Assert.Equal((1920 - 200 - 16, 16), WidgetGeometry.Corner(Work, 200, 100, left: false, top: true));
        Assert.Equal((16, 1040 - 100 - 16), WidgetGeometry.Corner(Work, 200, 100, left: true, top: false));
        Assert.Equal((1920 - 216, 1040 - 116), WidgetGeometry.Corner(Work, 200, 100, left: false, top: false));
    }

    [Fact]
    public void IsOnAnyScreen_uses_widget_center()
    {
        var screens = new[] { new PxRect(0, 0, 1920, 1080) };
        Assert.True(WidgetGeometry.IsOnAnyScreen(100, 100, 200, 100, screens));
        Assert.True(WidgetGeometry.IsOnAnyScreen(1800, 100, 200, 100, screens));    // metade para fora, centro dentro
        Assert.False(WidgetGeometry.IsOnAnyScreen(3000, 100, 200, 100, screens));   // monitor removido
        Assert.False(WidgetGeometry.IsOnAnyScreen(100, 100, 200, 100, Array.Empty<PxRect>()));
    }

    [Fact]
    public void AreaAt_prefers_containing_area_else_first()
    {
        var a = new PxRect(0, 0, 100, 100); var b = new PxRect(100, 0, 200, 100);
        Assert.Equal(b, WidgetGeometry.AreaAt(150, 50, new[] { a, b }));
        Assert.Equal(a, WidgetGeometry.AreaAt(999, 999, new[] { a, b }));
        Assert.Null(WidgetGeometry.AreaAt(0, 0, Array.Empty<PxRect>()));
    }
}
