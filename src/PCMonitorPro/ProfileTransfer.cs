using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCMonitorPro;

/// <summary>Partes do perfil que podem ser exportadas e importadas separadamente.</summary>
public enum ProfileSection { Appearance, Widgets, Alerts, History, Overlay, Diagnostic, Tray, Benchmarks }

public sealed class ProfileException : Exception
{
    public ProfileException(string message) : base(message) { }
}

/// <summary>Conteúdo do arquivo de perfil (.pmpperfil, JSON). Seção ausente = não exportada.</summary>
public sealed class ProfileFile
{
    // sem valor padrão: um arquivo de fora precisa declarar o formato e a versão
    public string Format { get; set; } = "";
    public int Version { get; set; }
    public DateTime ExportedAt { get; set; }
    public string? App { get; set; }
    /// <summary>Nome do computador de origem (só se o usuário quiser incluir).</summary>
    public string? Computer { get; set; }

    public AppearanceData? Appearance { get; set; }
    public WidgetsData? Widgets { get; set; }
    public AlertsData? Alerts { get; set; }
    public HistoryData? History { get; set; }
    public OverlayData? Overlay { get; set; }
    public DiagnosticData? Diagnostic { get; set; }
    public TrayData? Tray { get; set; }
    public BenchmarksData? Benchmarks { get; set; }

    public sealed class AppearanceData { public bool LightTheme { get; set; } }
    public sealed class WidgetsData
    {
        public List<WidgetConfig> Items { get; set; } = new();
        public int TempWarn { get; set; } = 75;
        public int TempBad { get; set; } = 88;
        public int PctWarn { get; set; } = 80;
        public int PctBad { get; set; } = 95;
    }
    public sealed class AlertsData
    {
        public bool Enabled { get; set; } = true;
        public int TempC { get; set; } = 90;
        public int RamPct { get; set; } = 95;
        public int CooldownS { get; set; } = 300;
    }
    public sealed class HistoryData { public bool Enabled { get; set; } = true; public int KeepDays { get; set; } = 14; }
    public sealed class OverlayData { public string Corner { get; set; } = "TL"; public double Scale { get; set; } = 1.0; public List<string>? Items { get; set; } }
    public sealed class DiagnosticData { public string Scenario { get; set; } = "geral"; }
    public sealed class TrayData { public bool CloseToTray { get; set; } }
    public sealed class BenchmarksData { public List<BenchRun> Runs { get; set; } = new(); public List<BenchRecord> Records { get; set; } = new(); }
}

/// <summary>
/// Exporta e importa um perfil (configurações e resultados de benchmark) como arquivo, para levar de um computador para outro
/// ou guardar em qualquer nuvem. NUNCA entram: chave da API, backups do Windows ("Restaurar originais"), perfis e programas dos
/// jogos (caminhos e comandos de um arquivo de fora virariam execução de programa), pasta de relatórios e marco do diagnóstico.
/// O arquivo importado é tratado como entrada não confiável: tamanho limitado, valores validados e limitados.
/// </summary>
public static class ProfileTransfer
{
    public const string FormatName = "PCMonitorPro.Perfil";
    public const int CurrentVersion = 1;
    public const string Extension = ".pmpperfil";
    public const long MaxBytes = 2 * 1024 * 1024;
    private const int MaxWidgets = 20, MaxMetricsPerWidget = 40;
    private static readonly string[] Corners = { "TL", "TR", "BL", "BR" };
    private static readonly double[] OverlayScales = { 0.85, 1.0, 1.3 };

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly IReadOnlyDictionary<ProfileSection, string> Names = new Dictionary<ProfileSection, string>
    {
        [ProfileSection.Appearance] = "Aparência (tema claro/escuro)",
        [ProfileSection.Widgets] = "Widgets flutuantes e cores dos limites",
        [ProfileSection.Alerts] = "Alertas de temperatura e RAM",
        [ProfileSection.History] = "Histórico de sensores (ligado e dias)",
        [ProfileSection.Overlay] = "Overlay de FPS (canto, tamanho, itens)",
        [ProfileSection.Diagnostic] = "Foco do diagnóstico",
        [ProfileSection.Tray] = "Bandeja do sistema",
        [ProfileSection.Benchmarks] = "Resultados de benchmark",
    };

    // ---------------------------------------------------------------- exportar

    public static string Export(AppSettings s, IEnumerable<ProfileSection> sections, bool includeComputerName, DateTime? now = null)
    {
        var want = sections.ToHashSet();
        var f = new ProfileFile
        {
            Format = FormatName, Version = CurrentVersion,
            ExportedAt = now ?? DateTime.Now,
            App = $"PC Monitor Pro {typeof(ProfileTransfer).Assembly.GetName().Version?.ToString(3)}",
            Computer = includeComputerName ? Environment.MachineName : null,
        };
        if (want.Contains(ProfileSection.Appearance)) f.Appearance = new() { LightTheme = s.LightTheme };
        if (want.Contains(ProfileSection.Widgets))
            f.Widgets = new()
            {
                Items = s.Widgets.Select(w => w.Clone()).ToList(),
                TempWarn = s.WidgetTempWarn, TempBad = s.WidgetTempBad, PctWarn = s.WidgetPctWarn, PctBad = s.WidgetPctBad,
            };
        if (want.Contains(ProfileSection.Alerts)) f.Alerts = new() { Enabled = s.AlertsEnabled, TempC = s.AlertTempC, RamPct = s.AlertRamPct, CooldownS = s.AlertCooldownS };
        if (want.Contains(ProfileSection.History)) f.History = new() { Enabled = s.HistoryEnabled, KeepDays = s.HistoryKeepDays };
        if (want.Contains(ProfileSection.Overlay)) f.Overlay = new() { Corner = s.OverlayCorner, Scale = s.OverlayScale, Items = s.OverlayItems?.ToList() };
        if (want.Contains(ProfileSection.Diagnostic)) f.Diagnostic = new() { Scenario = s.DiagScenario };
        if (want.Contains(ProfileSection.Tray)) f.Tray = new() { CloseToTray = s.CloseToTray };
        if (want.Contains(ProfileSection.Benchmarks)) f.Benchmarks = new() { Runs = s.BenchRuns.ToList(), Records = s.BenchRecords.ToList() };
        return JsonSerializer.Serialize(f, Json);
    }

    // ---------------------------------------------------------------- ler (entrada não confiável)

    public static ProfileFile Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ProfileException("O arquivo está vazio.");
        if (json.Length > MaxBytes) throw new ProfileException("O arquivo é grande demais para ser um perfil do PC Monitor Pro.");
        ProfileFile? f;
        try { f = JsonSerializer.Deserialize<ProfileFile>(json, Json); }
        catch (JsonException) { throw new ProfileException("O arquivo não é um perfil válido do PC Monitor Pro (JSON ilegível)."); }
        if (f == null || f.Format != FormatName) throw new ProfileException("Este arquivo não é um perfil do PC Monitor Pro.");
        if (f.Version > CurrentVersion)
            throw new ProfileException($"Este perfil é de uma versão mais nova (v{f.Version}). Atualize o PC Monitor Pro para importar.");
        if (f.Version < 1) throw new ProfileException("Versão de perfil inválida.");
        return f;
    }

    public static List<ProfileSection> Available(ProfileFile f)
    {
        var l = new List<ProfileSection>();
        if (f.Appearance != null) l.Add(ProfileSection.Appearance);
        if (f.Widgets != null) l.Add(ProfileSection.Widgets);
        if (f.Alerts != null) l.Add(ProfileSection.Alerts);
        if (f.History != null) l.Add(ProfileSection.History);
        if (f.Overlay != null) l.Add(ProfileSection.Overlay);
        if (f.Diagnostic != null) l.Add(ProfileSection.Diagnostic);
        if (f.Tray != null) l.Add(ProfileSection.Tray);
        if (f.Benchmarks != null) l.Add(ProfileSection.Benchmarks);
        return l;
    }

    // ---------------------------------------------------------------- aplicar

    /// <summary>Aplica as seções escolhidas em <paramref name="t"/>, validando cada valor. Seções que o arquivo não tem são ignoradas.</summary>
    public static void Apply(AppSettings t, ProfileFile f, IEnumerable<ProfileSection> sections)
    {
        var want = sections.ToHashSet();
        if (want.Contains(ProfileSection.Appearance) && f.Appearance is { } ap) t.LightTheme = ap.LightTheme;

        if (want.Contains(ProfileSection.Widgets) && f.Widgets is { } w)
        {
            var list = new List<WidgetConfig>();
            foreach (var item in (w.Items ?? new()).Where(i => i != null).Take(MaxWidgets))
            {
                var c = item.Clone();
                c.Id = Guid.NewGuid().ToString("N")[..8];   // sem repetir ids dos widgets que já existem
                c.Name = Cut(c.Name, 30);
                c.Metrics = (c.Metrics ?? new()).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => Cut(m, 200)).Distinct().Take(MaxMetricsPerWidget).ToList();
                c.X = Math.Clamp(c.X, -20000, 20000); c.Y = Math.Clamp(c.Y, -20000, 20000);
                c.Sanitize();
                list.Add(c);
            }
            t.Widgets = list;
            int tw = Math.Clamp(w.TempWarn, 1, 150), tb = Math.Clamp(w.TempBad, 1, 150), pw = Math.Clamp(w.PctWarn, 1, 150), pb = Math.Clamp(w.PctBad, 1, 150);
            if (tw < tb) { t.WidgetTempWarn = tw; t.WidgetTempBad = tb; }
            if (pw < pb) { t.WidgetPctWarn = pw; t.WidgetPctBad = pb; }
        }

        if (want.Contains(ProfileSection.Alerts) && f.Alerts is { } al)
        {
            t.AlertsEnabled = al.Enabled;
            t.AlertTempC = Math.Clamp(al.TempC, 50, 120);
            t.AlertRamPct = Math.Clamp(al.RamPct, 50, 100);
            t.AlertCooldownS = Math.Clamp(al.CooldownS, 10, 3600);
        }

        if (want.Contains(ProfileSection.History) && f.History is { } h)
        {
            t.HistoryEnabled = h.Enabled;
            t.HistoryKeepDays = Math.Clamp(h.KeepDays, 1, 365);
        }

        if (want.Contains(ProfileSection.Overlay) && f.Overlay is { } o)
        {
            t.OverlayCorner = Corners.Contains(o.Corner) ? o.Corner : "TL";
            // só as escalas que a tela do overlay oferece
            t.OverlayScale = double.IsFinite(o.Scale) ? OverlayScales.OrderBy(x => Math.Abs(x - o.Scale)).First() : 1.0;
            var valid = OverlayMetrics.All.Select(m => m.Key).ToHashSet();
            t.OverlayItems = o.Items?.Where(valid.Contains).Distinct().ToList();
        }

        if (want.Contains(ProfileSection.Diagnostic) && f.Diagnostic is { } d)
            t.DiagScenario = d.Scenario != null && Diagnostics.Scenarios.ContainsKey(d.Scenario) ? d.Scenario : "geral";

        if (want.Contains(ProfileSection.Tray) && f.Tray is { } tr) t.CloseToTray = tr.CloseToTray;

        if (want.Contains(ProfileSection.Benchmarks) && f.Benchmarks is { } b) MergeBenchmarks(t, b);
    }

    /// <summary>Junta os resultados do arquivo aos que já existem (sem repetir) e mantém os mesmos limites do programa:
    /// 10 execuções de CPU e 10 resultados por teste.</summary>
    private static void MergeBenchmarks(AppSettings t, ProfileFile.BenchmarksData b)
    {
        static bool Ok(double? v) => v is null || double.IsFinite(v.Value);
        var runs = (b.Runs ?? new()).Where(r => r != null && Ok(r.Multi) && Ok(r.Single))
            .Select(r => r with { Cpu = Cut(r.Cpu, 80) });
        t.BenchRuns = t.BenchRuns.Concat(runs).GroupBy(r => (r.Date, r.Multi, r.Single)).Select(g => g.First())
            .OrderByDescending(r => r.Date).Take(10).ToList();

        var recs = (b.Records ?? new()).Where(r => r != null && double.IsFinite(r.Score))
            .Select(r => r with { Test = Cut(r.Test, 30), Summary = Cut(r.Summary, 300) })
            .Where(r => r.Test.Length > 0);
        t.BenchRecords = t.BenchRecords.Concat(recs).GroupBy(r => (r.Date, r.Test)).Select(g => g.First())
            .OrderByDescending(r => r.Date).GroupBy(r => r.Test).SelectMany(g => g.Take(10)).OrderByDescending(r => r.Date).ToList();
    }

    private static string Cut(string? s, int max) => s == null ? "" : s.Length <= max ? s : s[..max];
}
