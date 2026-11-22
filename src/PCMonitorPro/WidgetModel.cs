using LibreHardwareMonitor.Hardware;

namespace PCMonitorPro;

/// <summary>Configuração de um widget flutuante (gravada em settings.json). Posição em pixels da tela virtual.</summary>
public sealed class WidgetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Widget";
    public bool Visible { get; set; } = true;
    public List<string> Metrics { get; set; } = new(WidgetCatalog.DefaultMetrics);
    public int X { get; set; } = 40;
    public int Y { get; set; } = 40;
    public double Opacity { get; set; } = 0.88;
    public double Scale { get; set; } = 1.0;
    public string Layout { get; set; } = "vertical";   // "vertical" | "horizontal"
    public bool Bars { get; set; } = true;
    public bool Title { get; set; } = true;
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public string Accent { get; set; } = "#4cc9f0";

    public bool Horizontal => string.Equals(Layout, "horizontal", StringComparison.OrdinalIgnoreCase);

    public WidgetConfig Clone()
    {
        var c = (WidgetConfig)MemberwiseClone();
        c.Metrics = new List<string>(Metrics);
        return c;
    }

    /// <summary>Corrige valores fora do intervalo (arquivo editado à mão ou antigo) sem perder o resto.</summary>
    public void Sanitize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N")[..8];
        if (string.IsNullOrWhiteSpace(Name)) Name = "Widget";
        Metrics = (Metrics ?? new()).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.2, 1.0) : 0.88;
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.6, 2.5) : 1.0;
        Layout = Horizontal ? "horizontal" : "vertical";
        if (!WidgetFormat.TryParseColor(Accent, out _)) Accent = "#4cc9f0";
    }
}

/// <summary>Um valor que pode aparecer no widget. Unit: %, °C, GB, MB, W, MHz, RPM, V, A, B/s ou "" (texto).</summary>
public sealed record WidgetMetric(string Id, string Group, string Label, string Short, string Unit, double? VMax, bool Alert)
{
    public override string ToString() => Label;
}

public enum WidgetLevel { Neutral, Ok, Warn, Bad }

/// <summary>Uma linha pronta para desenhar.</summary>
public sealed record WidgetRow(string Label, string Text, WidgetLevel Level, double? Ratio);

public static class WidgetCatalog
{
    public static readonly string[] DefaultMetrics = { "cpu.load", "cpu.temp", "ram.pct", "nv0.load", "nv0.temp" };
    /// <summary>Prefixo dos sensores individuais do LibreHardwareMonitor (id = prefixo + identificador do sensor).</summary>
    public const string SensorPrefix = "s:";

    public static readonly IReadOnlyList<WidgetMetric> Base = new WidgetMetric[]
    {
        new("cpu.load", "CPU", "CPU · uso", "CPU uso", "%", 100, true),
        new("cpu.temp", "CPU", "CPU · temperatura", "CPU temp", "°C", 110, true),
        new("cpu.power", "CPU", "CPU · consumo", "CPU watts", "W", null, false),
        new("ram.pct", "Memória", "RAM · em uso", "RAM", "%", 100, true),
        new("ram.used", "Memória", "RAM · usada", "RAM usada", "GB", null, false),
        new("nv0.load", "GPU", "GPU · uso", "GPU uso", "%", 100, true),
        new("nv0.temp", "GPU", "GPU · temperatura", "GPU temp", "°C", 110, true),
    };

    /// <summary>Unidade e se pinta por limite, a partir do tipo do sensor; null se o tipo não é suportado.</summary>
    private static (string Unit, double? VMax, bool Alert)? SensorInfo(SensorType t) => t switch
    {
        SensorType.Temperature => ("°C", 110, true),
        SensorType.Load => ("%", 100, true),
        SensorType.Control or SensorType.Level => ("%", 100, false),
        SensorType.Clock => ("MHz", null, false),
        SensorType.Power => ("W", null, false),
        SensorType.Voltage => ("V", null, false),
        SensorType.Current => ("A", null, false),
        SensorType.Fan => ("RPM", null, false),
        SensorType.Data => ("GB", null, false),
        SensorType.SmallData => ("MB", null, false),
        SensorType.Throughput => ("B/s", null, false),
        _ => null,
    };

    /// <summary>Base + um item por sensor lido (agrupado pelo hardware). Ids base primeiro.</summary>
    public static List<WidgetMetric> Build(SensorMonitor? m)
    {
        var list = new List<WidgetMetric>(Base);
        if (m == null) return list;
        foreach (var r in m.Rows.OrderBy(r => r.Hardware).ThenBy(r => r.Order).ThenBy(r => r.Name))
        {
            if (SensorInfo(r.Type) is not { } info) continue;
            list.Add(new WidgetMetric(SensorPrefix + r.Id, r.Hardware, $"{r.Hardware} · {r.Name} ({SensorRow.CategoryName(r.Type)})",
                r.Name, info.Unit, info.VMax, info.Alert));
        }
        return list;
    }

    /// <summary>Valores atuais, por id de métrica (base e sensores). Ausente = sem leitura.</summary>
    public static Dictionary<string, float?> ReadValues(SensorMonitor m)
    {
        var rows = m.Rows.ToList();
        var v = new Dictionary<string, float?>
        {
            ["cpu.load"] = m.CpuLoad,
            ["cpu.temp"] = m.CpuTemperature,
            ["cpu.power"] = m.CpuPower,
            ["ram.pct"] = HistorySampler.RamPercent(m),
            ["ram.used"] = rows.FirstOrDefault(r => r.HardwareType == HardwareType.Memory && r.Type == SensorType.Data && r.Name == "Memory Used")?.RawValue,
        };
        var gpu = HistorySampler.Sample(m, null);
        v["nv0.load"] = gpu["nv0.load"] as float?;
        v["nv0.temp"] = gpu["nv0.temp"] as float?;
        foreach (var r in rows) v[SensorPrefix + r.Id] = r.RawValue;
        return v;
    }
}

public static class WidgetFormat
{
    /// <summary>Limites de cor (amarelo, vermelho), iguais aos do PC Monitor original.</summary>
    public const double TempWarn = 75, TempBad = 88, PctWarn = 80, PctBad = 95;

    public static string FormatValue(WidgetMetric? m, double? v)
    {
        if (v is not { } x || double.IsNaN(x)) return "—";
        if (m == null) return x.ToString("0.#");
        return m.Unit switch
        {
            "B/s" => x >= 1 << 30 ? $"{x / (1 << 30):0.0} GB/s" : x >= 1 << 20 ? $"{x / (1 << 20):0.0} MB/s" : x >= 1 << 10 ? $"{x / (1 << 10):0.0} KB/s" : $"{x:0} B/s",
            "%" => x < 10 ? $"{x:0.0}%" : $"{x:0}%",
            "°C" => $"{x:0} °C",
            "GB" => $"{x:0.0} GB",
            "MB" => $"{x:0} MB",
            "W" => $"{x:0.0} W",
            "MHz" => $"{x:0} MHz",
            "RPM" => $"{x:0} RPM",
            "V" => $"{x:0.000} V",
            "A" => $"{x:0.000} A",
            _ => x.ToString("0.#"),
        };
    }

    public static WidgetLevel LevelFor(WidgetMetric? m, double? v)
    {
        if (m == null || v is not { } x || !m.Alert) return WidgetLevel.Neutral;
        (double warn, double bad)? t = m.Unit == "°C" ? (TempWarn, TempBad) : m.Unit == "%" ? (PctWarn, PctBad) : null;
        if (t is not { } lim) return WidgetLevel.Neutral;
        return x >= lim.bad ? WidgetLevel.Bad : x >= lim.warn ? WidgetLevel.Warn : WidgetLevel.Ok;
    }

    public static List<WidgetRow> BuildRows(WidgetConfig cfg, IReadOnlyDictionary<string, WidgetMetric> catalog, IReadOnlyDictionary<string, float?> values)
    {
        var rows = new List<WidgetRow>();
        foreach (var id in cfg.Metrics)
        {
            catalog.TryGetValue(id, out var m);
            values.TryGetValue(id, out var v);
            double? ratio = m is { VMax: > 0 } && v is { } x ? Math.Clamp(x / m.VMax.Value, 0, 1) : null;
            rows.Add(new WidgetRow(m?.Short ?? id, FormatValue(m, v), LevelFor(m, v), ratio));
        }
        return rows;
    }

    public static bool TryParseColor(string? s, out (byte R, byte G, byte B) rgb)
    {
        rgb = default;
        if (s is not { Length: 7 } || s[0] != '#') return false;
        return byte.TryParse(s.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out rgb.R)
            && byte.TryParse(s.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out rgb.G)
            && byte.TryParse(s.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out rgb.B);
    }
}

/// <summary>Retângulo em pixels (Right/Bottom exclusivos), usado para a área útil dos monitores.</summary>
public readonly record struct PxRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>Posicionamento do widget: cantos magnéticos, cantos escolhidos e "não deixar fora da tela".</summary>
public static class WidgetGeometry
{
    public const int SnapMargin = 14, CornerMargin = 16;

    /// <summary>Gruda nas bordas da área útil quando estiver a menos de <paramref name="margin"/> px.</summary>
    public static (int X, int Y) Snap(int x, int y, int w, int h, PxRect work, int margin = SnapMargin)
    {
        if (Math.Abs(x - work.Left) < margin) x = work.Left;
        else if (Math.Abs(x + w - work.Right) < margin) x = work.Right - w;
        if (Math.Abs(y - work.Top) < margin) y = work.Top;
        else if (Math.Abs(y + h - work.Bottom) < margin) y = work.Bottom - h;
        return (x, y);
    }

    public static (int X, int Y) Corner(PxRect work, int w, int h, bool left, bool top, int margin = CornerMargin) =>
        (left ? work.Left + margin : work.Right - w - margin, top ? work.Top + margin : work.Bottom - h - margin);

    /// <summary>O centro do widget está em algum monitor?</summary>
    public static bool IsOnAnyScreen(int x, int y, int w, int h, IEnumerable<PxRect> screens)
    {
        int cx = x + w / 2, cy = y + h / 2;
        return screens.Any(s => s.Contains(cx, cy));
    }

    /// <summary>Área que contém o ponto (ou a primeira, se nenhuma); null se não há monitores.</summary>
    public static PxRect? AreaAt(int x, int y, IReadOnlyList<PxRect> areas) =>
        areas.Count == 0 ? null : areas.Cast<PxRect?>().FirstOrDefault(a => a!.Value.Contains(x, y)) ?? areas[0];
}
