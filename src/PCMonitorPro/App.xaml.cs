using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace PCMonitorPro;

public partial class App : Application
{
    // Uma única instância: abrir o PC Monitor Pro de novo só traz a janela que já está aberta para a frente.
    private static Mutex? _single;
    private const string ShowEventName = @"Local\PCMonitorPro.Show";
    private static EventWaitHandle? _showEvent;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    /// <summary>Os testes criam um Application, e o WPF chama OnStartup. Sem isto, com o programa real aberto, o teste sinalizaria a
    /// instância do usuário (mostrando a janela dela) e se encerraria em seguida.</summary>
    internal static bool IsUnderTest { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (IsUnderTest) { base.OnStartup(e); return; }
        AppLog.InstallGlobalHandlers(this);
        AppLog.Info($"PC Monitor Pro iniciado (v{typeof(App).Assembly.GetName().Version}, {Environment.OSVersion.Version})");
        bool first;
        try { _single = new Mutex(true, @"Local\PCMonitorPro.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; } // criado por outra instância com outro nível de permissão

        if (!first)
        {
            // Pede à instância que já roda para aparecer (ela pode estar escondida na bandeja, onde FindWindow não ajuda).
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); Shutdown(); return; }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException) { }
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
        ListenForSecondInstance();
        new MainWindow().Show();
    }

    /// <summary>Uma segunda abertura do programa sinaliza este evento; a janela volta a aparecer.</summary>
    private void ListenForSecondInstance()
    {
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var ev = _showEvent;
            new Thread(() =>
            {
                while (ev.WaitOne())
                {
                    var app = Current;
                    if (app == null) break;
                    app.Dispatcher.BeginInvoke(new Action(() => (app.MainWindow as MainWindow)?.ShowFromTray()));
                }
            }) { IsBackground = true, Name = "segunda-instancia" }.Start();
        }
        catch (Exception ex) { AppLog.Warn("Não foi possível escutar uma segunda abertura", ex); }
    }
}
