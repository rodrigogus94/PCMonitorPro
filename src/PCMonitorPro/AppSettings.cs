using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PCMonitorPro;

public sealed record BenchRun(DateTime Date, double? Multi, double? Single, bool Sustained, string Cpu);

/// <summary>Resultado dos benchmarks de jogo (Test = "gpu", "sim" ou "mem"). Score é usado para a barra do histórico.</summary>
public sealed record BenchRecord(DateTime Date, string Test, double Score, string Summary);

/// <summary>Programa extra que abre antes do jogo (ex.: ferramenta de comando/monitoramento) e pode fechar junto com ele.</summary>
public sealed class HelperProgram
{
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
    public bool CloseWithGame { get; set; } = true;
    public string Display => System.IO.Path.GetFileName(Path) + (string.IsNullOrWhiteSpace(Args) ? "" : " " + Args);
}

/// <summary>Perfil de um jogo na aba JOGOS: desempenho, argumentos de gráficos e programas auxiliares.</summary>
public sealed class GameProfile
{
    public string Priority { get; set; } = "normal";   // normal | above | high
    public bool HighPerfPlan { get; set; }
    public bool GpuHigh { get; set; }
    public bool DisableFso { get; set; }
    public string? ExePath { get; set; }
    public string WinMode { get; set; } = "default";   // default | full | borderless | window
    public string Res { get; set; } = "default";       // default | 1280x720 | 1920x1080 | 2560x1440 | 3840x2160
    public int Hz { get; set; }                        // 0 = não definir
    public string Style { get; set; } = "source";      // source | unity | unreal
    public string Extra { get; set; } = "";
    public List<HelperProgram> Helpers { get; set; } = new();
}

/// <summary>Jogo adicionado à mão (qualquer .exe).</summary>
public sealed class CustomGame
{
    public string Name { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Args { get; set; } = "";
}

/// <summary>Configurações em %AppData%\PCMonitorPro. A chave da API é cifrada com DPAPI (só este usuário do Windows a lê).</summary>
public sealed class AppSettings
{
    public static string Folder { get; internal set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCMonitorPro");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    public string? HowManyFpsKeyProtected { get; set; }

    // BENCHMARK: últimos resultados (mais recente primeiro)
    public List<BenchRun> BenchRuns { get; set; } = new();
    public List<BenchRecord> BenchRecords { get; set; } = new();

    // OTIMIZAR: valores originais dos ajustes do Windows antes da primeira alteração ("<ausente>" = não existia)
    public Dictionary<string, string?> TweakBackup { get; set; } = new();
    /// <summary>ComDB original (reservas de números COM) antes da primeira "liberação".</summary>
    public byte[]? ComDbBackup { get; set; }
    /// <summary>Programas de inicialização removidos, para "Restaurar removidos".</summary>
    public List<StartupBackup> StartupRemoved { get; set; } = new();

    // JOGOS
    public Dictionary<string, GameProfile> GameProfiles { get; set; } = new();
    public List<CustomGame> CustomGames { get; set; } = new();

    // Diagnóstico: marco de comparação ("antes/depois de uma mudança") e foco escolhido
    public DateTime? MilestoneTime { get; set; }
    public string MilestoneNote { get; set; } = "";
    public string DiagScenario { get; set; } = "geral";

    // Sincronização com o Google (opcional). Tokens e segredo ficam cifrados com DPAPI; nunca vão no perfil exportado.
    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecretProtected { get; set; }
    public string? GoogleRefreshTokenProtected { get; set; }
    public string? GoogleEmail { get; set; }
    /// <summary>Identificador deste computador na nuvem (aleatório; não é o nome da máquina).</summary>
    public string? MachineId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? GoogleClientSecret { get => Unprotect(GoogleClientSecretProtected); set => GoogleClientSecretProtected = Protect(value); }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? GoogleRefreshToken { get => Unprotect(GoogleRefreshTokenProtected); set => GoogleRefreshTokenProtected = Protect(value); }

    public string EnsureMachineId() => MachineId ??= Guid.NewGuid().ToString("N");

    internal static string? Protect(string? v) => string.IsNullOrWhiteSpace(v) ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(v.Trim()), null, DataProtectionScope.CurrentUser));

    internal static string? Unprotect(string? v)
    {
        if (string.IsNullOrEmpty(v)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v), null, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }

    // Bandeja: fechar a janela mantém o programa rodando (widgets, histórico e alertas)
    public bool CloseToTray { get; set; }
    public bool TrayHintShown { get; set; }

    // Widgets flutuantes (nenhum até o usuário adicionar)
    public List<WidgetConfig> Widgets { get; set; } = new();

    // Histórico de sensores (detecção de queda)
    public bool HistoryEnabled { get; set; } = true;
    public int HistoryKeepDays { get; set; } = 14;

    // Pasta dos relatórios do diagnóstico (vazio = Documentos\PCMonitor)
    public string? ReportFolder { get; set; }

    // Limites de cor dos widgets: amarelo no primeiro, vermelho no segundo
    public int WidgetTempWarn { get; set; } = 75;
    public int WidgetTempBad { get; set; } = 88;
    public int WidgetPctWarn { get; set; } = 80;
    public int WidgetPctBad { get; set; } = 95;

    // Alertas de temperatura/RAM (aviso no canto da tela, com cooldown por sensor)
    public bool AlertsEnabled { get; set; } = true;
    public int AlertTempC { get; set; } = 90;
    public int AlertRamPct { get; set; } = 95;
    public int AlertCooldownS { get; set; } = 300;

    // Aparência
    public bool LightTheme { get; set; }

    // Overlay
    public string OverlayCorner { get; set; } = "TL";
    public double OverlayScale { get; set; } = 1.0;
    /// <summary>Itens escolhidos para o overlay (chaves de OverlayMetrics); null = padrão.</summary>
    public List<string>? OverlayItems { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] // nunca gravar a chave em texto puro
    public string? HowManyFpsKey
    {
        get
        {
            if (string.IsNullOrEmpty(HowManyFpsKeyProtected)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(HowManyFpsKeyProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return null; }
        }
        set
        {
            HowManyFpsKeyProtected = string.IsNullOrWhiteSpace(value)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }
    }

    private static readonly object SaveGate = new();

    /// <summary>Verdadeiro quando o arquivo existe mas não deu para lê-lo por erro de E/S (ex.: preso por outro programa):
    /// nesse caso Save() não sobrescreve, para não destruir os backups de "Restaurar".</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool LoadFailedKeepFile { get; private set; }

    /// <summary>Carrega as configurações. Arquivo ilegível vira cópia ".corrompido-data" e tenta o último arquivo bom (.bak).</summary>
    public static AppSettings Load()
    {
        string path = FilePath;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (IOException ex)
        {
            AppLog.Error("Não foi possível ler o settings.json (E/S); ele não será sobrescrito.", ex);
            return new AppSettings { LoadFailedKeepFile = true };
        }
        catch (Exception ex)   // JSON inválido ou campos com tipo errado
        {
            AppLog.Error("settings.json ilegível; guardando uma cópia e tentando o último arquivo bom.", ex);
            try { File.Copy(path, $"{path}.corrompido-{DateTime.Now:yyyyMMdd-HHmmss}", true); } catch (Exception cex) { AppLog.Debug(cex); }
            try { if (File.Exists(path + ".bak")) return Parse(File.ReadAllText(path + ".bak")); }
            catch (Exception bex) { AppLog.Warn("O .bak também está ilegível.", bex); }
            return new AppSettings();
        }
    }

    internal static AppSettings Parse(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json) ?? throw new JsonException("arquivo vazio");

    /// <summary>Grava de forma atômica (arquivo temporário + troca), mantendo o arquivo anterior como .bak.</summary>
    public void Save()
    {
        if (LoadFailedKeepFile) return;
        lock (SaveGate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = FilePath, tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
            }
            catch (Exception ex) { AppLog.Error("Não foi possível salvar o settings.json", ex); }
        }
    }
}
