using System.Diagnostics;
using System.IO;
using System.Text;

namespace PCMonitorPro;

/// <summary>Executa UM comando de PowerShell escolhido pelo usuário (depois de confirmação na tela) e entrega a saída aos poucos.</summary>
public static class CommandRunner
{
    private static Process? _active;
    public static bool Running => _active is { HasExited: false };

    public static async Task<int> RunAsync(string command, Action<string> onOutput, CancellationToken ct = default)
    {
        // Script por stdin (não vai para a linha de comando nem para arquivo), como no coletor de diagnóstico.
        string script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; & { " +
                        command + "\n} 2>&1 | Out-String -Width 200 -Stream";
        var psi = new ProcessStartInfo(DiagnosticsCollector.PowerShellExe, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell.");
        _active = proc;
        try
        {
            async Task Pump(StreamReader r)
            {
                var buf = new char[2048]; int n;
                while ((n = await r.ReadAsync(buf, 0, buf.Length)) > 0) onOutput(new string(buf, 0, n).Replace("\r\n", "\n"));
            }
            var t1 = Pump(proc.StandardOutput); var t2 = Pump(proc.StandardError);
            // Linha em branco no fim: sem ela o PowerShell em modo stdin descarta o script em silêncio.
            await proc.StandardInput.WriteAsync(script + "\n\r\n");
            proc.StandardInput.Close();
            try { await proc.WaitForExitAsync(ct); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch (Exception ex) { AppLog.Warn("Comando: falha ao cancelar", ex); }
                throw;
            }
            await Task.WhenAll(t1, t2);
            return proc.ExitCode;
        }
        finally { _active = null; }
    }

    public static void Cancel()
    {
        try { _active?.Kill(true); } catch (Exception ex) { AppLog.Warn("Comando: falha ao cancelar", ex); }
    }
}
