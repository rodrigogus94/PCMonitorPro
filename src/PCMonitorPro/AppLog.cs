using System.IO;
using System.Text;

namespace PCMonitorPro;

/// <summary>
/// Log em arquivo (%AppData%\PCMonitorPro\pcmonitor.log, rotação em 1 MB). Nunca lança exceção.
/// Mensagens repetidas (mesmo tipo + texto) são gravadas no máximo uma vez por minuto, para o log não encher
/// quando um sensor falha a cada segundo.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (DateTime Last, int Skipped)> Recent = new();
    private static string? _pathOverride;

    public static string FilePath => _pathOverride ?? Path.Combine(AppSettings.Folder, "pcmonitor.log");

    /// <summary>Usado nos testes para gravar em outra pasta.</summary>
    internal static void UsePath(string? path) { lock (Gate) { _pathOverride = path; Recent.Clear(); } }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERRO", message, ex);
    /// <summary>Exceção engolida de propósito (leitura de sensor, registro ausente...): fica no log sem incomodar.</summary>
    public static void Debug(Exception ex, [System.Runtime.CompilerServices.CallerMemberName] string? where = null,
                             [System.Runtime.CompilerServices.CallerFilePath] string? file = null)
        => Write("DEBUG", $"{Path.GetFileNameWithoutExtension(file)}.{where}", ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            string key = $"{level}|{message}|{ex?.GetType().Name}|{ex?.Message}";
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                int skipped = 0;
                if (Recent.TryGetValue(key, out var r))
                {
                    if (now - r.Last < TimeSpan.FromMinutes(1)) { Recent[key] = (r.Last, r.Skipped + 1); return; }
                    skipped = r.Skipped;
                }
                if (Recent.Count > 500) Recent.Clear();
                Recent[key] = (now, 0);

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(' ').Append(level).Append(' ').Append(message);
                if (skipped > 0) sb.Append($" (+{skipped} repetições)");
                if (ex != null) sb.Append(" :: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                if (ex != null && level is "ERRO" or "FATAL") sb.AppendLine().Append(ex.StackTrace);
                sb.AppendLine();

                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    string old = path + ".1";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
        }
        catch { /* o log nunca pode derrubar o programa */ }
    }

    /// <summary>Instala os handlers globais: exceções não tratadas (inclusive de async void e tarefas) vão para o log.</summary>
    public static void InstallGlobalHandlers(System.Windows.Application app)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            Error("Exceção não tratada na interface", e.Exception);
            e.Handled = true;   // não derruba o programa por um erro num clique; o detalhe fica no log
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("FATAL", "Exceção fatal", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Error("Exceção em tarefa não observada", e.Exception);
            e.SetObserved();
        };
    }
}
