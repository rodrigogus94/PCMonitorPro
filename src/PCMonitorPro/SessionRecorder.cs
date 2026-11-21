using System.Diagnostics;
using System.IO;
using LibreHardwareMonitor.Hardware;

namespace PCMonitorPro;

/// <summary>Colunas e leitura dos sensores para o histórico em CSV (ids iguais aos do PC Monitor original).</summary>
public static class HistorySampler
{
    public static readonly IReadOnlyDictionary<string, HistoryColumn> Catalog = new Dictionary<string, HistoryColumn>
    {
        ["cpu.temp"] = new("CPU temperatura", "°C"),
        ["cpu.load"] = new("CPU uso", "%"),
        ["cpu.power"] = new("CPU consumo", "W"),
        ["ram.pct"] = new("RAM em uso", "%"),
        ["nv0.temp"] = new("GPU temperatura", "°C"),
        ["nv0.load"] = new("GPU uso", "%"),
        ["proc.top"] = new("Programa com mais RAM", ""),
    };

    private static bool IsGpu(HardwareType t) => t is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    public static Dictionary<string, object?> Sample(SensorMonitor m, string? topProc)
    {
        var rows = m.Rows.ToList();
        // GPU dedicada primeiro (NVIDIA/AMD); a integrada Intel só se for a única
        HardwareType? gpuType = rows.Select(r => r.HardwareType).Where(IsGpu).OrderBy(t => t == HardwareType.GpuIntel).Cast<HardwareType?>().FirstOrDefault();
        float? Gpu(SensorType st) => gpuType == null ? null
            : rows.FirstOrDefault(r => r.HardwareType == gpuType && r.Type == st && r.Name == "GPU Core")?.RawValue;
        return new Dictionary<string, object?>
        {
            ["cpu.temp"] = m.CpuTemperature,
            ["cpu.load"] = m.CpuLoad,
            ["cpu.power"] = m.CpuPower,
            ["ram.pct"] = rows.FirstOrDefault(r => r.HardwareType == HardwareType.Memory && r.Type == SensorType.Load && r.Name == "Memory")?.RawValue,
            ["nv0.temp"] = Gpu(SensorType.Temperature),
            ["nv0.load"] = Gpu(SensorType.Load),
            ["proc.top"] = topProc,
        };
    }

    /// <summary>Nome do programa que mais usa RAM (soma por nome). Lento (varre os processos): chame de vez em quando.</summary>
    public static string? TopMemoryProcess()
    {
        try
        {
            var procs = Process.GetProcesses();
            try
            {
                return procs.Select(p => { try { return (Name: p.ProcessName, Ws: p.WorkingSet64); } catch { return (Name: "", Ws: 0L); } })
                    .Where(x => x.Name != "").GroupBy(x => x.Name).OrderByDescending(g => g.Sum(x => x.Ws))
                    .Select(g => $"{g.Key} ({g.Sum(x => x.Ws) / 1048576} MB)").FirstOrDefault();
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        catch (Exception ex) { AppLog.Debug(ex); return null; }
    }
}

/// <summary>
/// Grava uma linha de histórico a cada <see cref="Interval"/> segundos enquanto o programa roda. Ao iniciar, informa a sessão
/// anterior que não terminou normalmente (queda, tela azul, reinício seco ou fechamento forçado).
/// </summary>
public sealed class SessionRecorder : IDisposable
{
    public const int Interval = 5;
    public const int KeepDays = 14;
    private readonly HistoryLogger _logger;
    private int _ticks;
    private string? _topProc;
    private Task? _topTask;

    public string Folder { get; }
    public string? CurrentPath => _logger.Path;
    /// <summary>Sessão anterior sem a marca de fim normal, achada ao iniciar (null = nenhuma).</summary>
    public SessionInfo? PreviousUnclean { get; private set; }

    public static string DefaultFolder => Path.Combine(AppSettings.Folder, "historico");

    public SessionRecorder(string? folder = null)
    {
        Folder = folder ?? DefaultFolder;
        _logger = new HistoryLogger(Folder);
    }

    /// <summary>Apaga sessões antigas, procura a queda anterior e abre uma nova sessão.</summary>
    public void Start()
    {
        try
        {
            HistoryLogger.Cleanup(Folder, KeepDays);
            PreviousUnclean = HistoryLogger.FindUnclean(Folder);
            _logger.Start(HistorySampler.Catalog);
        }
        catch (Exception ex) { AppLog.Warn("Histórico: não foi possível iniciar a sessão", ex); }
    }

    /// <summary>Chame a cada segundo; grava a cada <see cref="Interval"/> chamadas.</summary>
    public void Tick(SensorMonitor monitor)
    {
        if (!_logger.Active || !monitor.IsOpen) return;
        if (++_ticks % Interval != 0) return;
        if (_ticks % (Interval * 6) == Interval && (_topTask == null || _topTask.IsCompleted))
            _topTask = Task.Run(() => _topProc = HistorySampler.TopMemoryProcess());
        try { _logger.Write(HistorySampler.Sample(monitor, _topProc)); }
        catch (Exception ex) { AppLog.Debug(ex); }
    }

    public void Dispose() => _logger.Close();
}
