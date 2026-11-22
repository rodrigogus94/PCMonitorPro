namespace PCMonitorPro;

/// <summary>Temperatura lida (uma por componente) para checar contra o limite.</summary>
public readonly record struct TempReading(string Key, string Label, float Value);

/// <summary>
/// Decide quando avisar de temperatura/RAM altas. Cada alerta tem um cooldown próprio: o mesmo sensor só avisa de novo
/// depois de <see cref="AppSettings.AlertCooldownS"/> segundos. Vários sensores acima do limite viram uma só mensagem.
/// </summary>
public sealed class AlertMonitor
{
    /// <summary>Acima disto é lixo de sensor (comum em placas-mãe), não temperatura real.</summary>
    public const float MaxPlausibleTemp = 125f;
    public const int MaxLines = 4;

    private readonly Dictionary<string, DateTime> _last = new();

    /// <summary>Lista de linhas a avisar agora (vazia = nada). Já registra o cooldown das que devolve.</summary>
    public List<string> Evaluate(AppSettings cfg, IEnumerable<TempReading> temps, float? ramPct, DateTime now)
    {
        var due = new List<string>();
        if (!cfg.AlertsEnabled) return due;
        var cooldown = TimeSpan.FromSeconds(Math.Max(10, cfg.AlertCooldownS));

        void Check(string key, string msg)
        {
            if (_last.TryGetValue(key, out var t) && now - t < cooldown) return;
            _last[key] = now;
            due.Add(msg);
        }

        foreach (var t in temps)
            if (t.Value >= cfg.AlertTempC && t.Value <= MaxPlausibleTemp)
                Check(t.Key, $"{t.Label} em {t.Value:0} °C (limite {cfg.AlertTempC} °C)");
        if (ramPct is { } r && r >= cfg.AlertRamPct)
            Check("ram", $"RAM em {r:0}% (limite {cfg.AlertRamPct}%)");
        return due;
    }

    /// <summary>Texto único para o aviso: até <see cref="MaxLines"/> linhas e "(+N outros)".</summary>
    public static string Format(IReadOnlyList<string> lines) =>
        string.Join("\n", lines.Take(MaxLines)) + (lines.Count > MaxLines ? $"\n(+{lines.Count - MaxLines} outros)" : "");

    /// <summary>Maior temperatura por componente (CPU, GPU, discos…), para não alertar por cada núcleo.</summary>
    public static List<TempReading> TempsFrom(SensorMonitor m) =>
        m.Rows.Where(r => r.Type == LibreHardwareMonitor.Hardware.SensorType.Temperature && r.RawValue is > 0)
              .GroupBy(r => r.Hardware)
              .Select(g => g.OrderByDescending(r => r.RawValue).First())
              .Select(r => new TempReading(r.Hardware, r.Hardware, r.RawValue!.Value))
              .ToList();
}
