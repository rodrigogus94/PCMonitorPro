using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace PCMonitorPro;

public partial class App : Application
{
    // Uma única instância: abrir o PC Monitor Pro de novo só traz a janela que já está aberta para a frente.
    private static Mutex? _single;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    protected override void OnStartup(StartupEventArgs e)
    {
        AppLog.InstallGlobalHandlers(this);
        AppLog.Info($"PC Monitor Pro iniciado (v{typeof(App).Assembly.GetName().Version}, {Environment.OSVersion.Version})");
        bool first;
        try { _single = new Mutex(true, @"Local\PCMonitorPro.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; } // criado por outra instância com outro nível de permissão

        if (!first)
        {
            IntPtr h = FindWindow(null, "PC Monitor Pro");
            if (h != IntPtr.Zero)
            {
                if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                SetForegroundWindow(h);
            }
            Shutdown();
            return;
        }
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
