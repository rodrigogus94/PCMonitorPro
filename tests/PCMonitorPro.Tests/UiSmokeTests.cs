using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace PCMonitorPro.Tests;

/// <summary>Instancia a janela principal (sem mostrar) para validar o XAML e as páginas Diagnóstico/Comandos.</summary>
[Collection("pasta-de-dados")]
public class UiSmokeTests : IDisposable
{
    private readonly string _old = AppSettings.Folder;
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "pmp-ui-" + Guid.NewGuid().ToString("N"));

    public UiSmokeTests() { Directory.CreateDirectory(_tmp); AppSettings.Folder = _tmp; AppLog.UsePath(Path.Combine(_tmp, "t.log")); }
    public void Dispose() { AppSettings.Folder = _old; AppLog.UsePath(null); try { Directory.Delete(_tmp, true); } catch { } }

    private static System.Windows.Threading.Dispatcher? _ui;
    private static readonly object UiLock = new();

    /// <summary>O WPF só permite uma Application por processo e ela morre com a thread que a criou: todos os testes de
    /// interface rodam na mesma thread de UI, criada uma vez.</summary>
    private static System.Windows.Threading.Dispatcher Ui()
    {
        lock (UiLock)
        {
            if (_ui != null) return _ui;
            var ready = new ManualResetEventSlim();
            var t = new Thread(() =>
            {
                var disp = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                // a janela dispara tarefas assíncronas no construtor; as continuações precisam voltar para a thread de UI
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(disp));
                _ui = disp;
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            }) { IsBackground = true, Name = "ui-tests" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            ready.Wait();
            return _ui!;
        }
    }

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var done = new ManualResetEventSlim();
        Ui().BeginInvoke(new Action(() =>
        {
            try { body(); } catch (Exception ex) { error = ex; }
            // dá tempo das continuações pendentes rodarem antes de seguir para o próximo teste
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += (_, _) => { timer.Stop(); done.Set(); };
            timer.Start();
        }));
        Assert.True(done.Wait(TimeSpan.FromSeconds(60)), "a janela travou");
        if (error != null) throw new Exception("Falha na thread de UI: " + error, error);
    }

    private static T Field<T>(object o, string name) =>
        (T)(o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(o)
            ?? o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(o)
            ?? throw new InvalidOperationException("sem campo " + name));

    private static void Call(object o, string name) =>
        o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(o, null);

    [Fact]
    public void MainWindow_Builds_AndDiagnosticPagesWork()
    {
        RunSta(() =>
        {
            EnsureApp();
            var w = new MainWindow();
            try
            {
                Assert.Equal("v0.17.0", Field<TextBlock>(w, "SideVersion").Text);   // do csproj, não fixo
                Assert.DoesNotContain("RESET", Field<TextBlock>(w, "TitlePage").Text);
                Call(w, "InitDiag");
                var chips = Field<WrapPanel>(w, "DiagScenarios");
                Assert.Equal(Diagnostics.Scenarios.Count, chips.Children.Count);
                Assert.Contains("Nenhum marco", Field<TextBlock>(w, "DiagMilestoneText").Text);

                Call(w, "InitSettings");
                Assert.True(Field<CheckBox>(w, "AlertEnabled").IsChecked);
                Assert.Equal("90", Field<TextBox>(w, "AlertTemp").Text);
                Assert.Equal("95", Field<TextBox>(w, "AlertRam").Text);
                Assert.NotNull(Field<CheckBox>(w, "AutoStartCheck"));
                Assert.True(Field<CheckBox>(w, "HistEnabled").IsChecked);
                Assert.Equal("14", Field<TextBox>(w, "HistDays").Text);
                Assert.Equal("75", Field<TextBox>(w, "LimTempWarn").Text);
                Assert.Contains("Pasta padrão", Field<TextBlock>(w, "ReportFolderText").Text);
                Assert.Contains("PC Monitor Pro v0.17.0", Field<TextBlock>(w, "AboutText").Text);

                Call(w, "RefreshCommands");
                var list = Field<ListBox>(w, "CmdList");
                Assert.True(list.Items.Count > 10);
                Assert.NotNull(list.SelectedItem);
                Assert.False(string.IsNullOrWhiteSpace(Field<TextBox>(w, "CmdText").Text));
                Assert.Contains("Foco:", Field<TextBlock>(w, "CmdContext").Text);
            }
            finally { w.Close(); }
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);

    private static void EnsureApp()
    {
        if (Application.Current != null) return;
        // URIs de pacote relativas (Assets/logo.png) precisam apontar para o assembly do programa, não para o testhost
        typeof(Application).GetField("_resourceAssembly", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, typeof(MainWindow).Assembly);
        App.IsUnderTest = true;   // sem instância única nem janela extra: veja App.OnStartup
        var app = new App();
        app.InitializeComponent();
        // sem isto o WPF encerra o Application quando o primeiro teste fecha sua janela, e não dá para criar outro no processo
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    [Fact]
    public void WidgetWindow_ShowsRows_MovesInPixels_AndTogglesClickThrough()
    {
        RunSta(() =>
        {
            EnsureApp();
            var cfg = new WidgetConfig { Name = "Teste", X = 120, Y = 130, Metrics = new() { "cpu.load", "cpu.temp", "ram.pct" } };
            var w = new WidgetWindow(cfg);
            try
            {
                w.Show();
                w.SetRows(new List<WidgetRow>
                {
                    new("CPU uso", "42%", WidgetLevel.Ok, 0.42), new("CPU temp", "90 °C", WidgetLevel.Bad, 0.8), new("RAM", "—", WidgetLevel.Neutral, null),
                });
                w.UpdateLayout();
                Assert.True(w.ActualWidth > 100 && w.ActualHeight > 40);

                w.MovePx(200, 210);
                var r = w.RectPx();
                Assert.Equal((200, 210), (r.X, r.Y));

                const int WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOOLWINDOW = 0x80;
                var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                int ex = GetWindowLong(h, -20);
                Assert.Equal(0, ex & WS_EX_TRANSPARENT);
                Assert.NotEqual(0, ex & WS_EX_NOACTIVATE);   // não rouba o foco
                Assert.NotEqual(0, ex & WS_EX_TOOLWINDOW);   // fora do Alt+Tab

                cfg.ClickThrough = true; w.Configure(cfg);
                Assert.NotEqual(0, GetWindowLong(h, -20) & WS_EX_TRANSPARENT);
                cfg.ClickThrough = false; w.Configure(cfg);
                Assert.Equal(0, GetWindowLong(h, -20) & WS_EX_TRANSPARENT);

                // horizontal, sem barras e sem título também montam
                cfg.Layout = "horizontal"; cfg.Bars = false; cfg.Title = false; w.Configure(cfg);
                w.SetRows(new List<WidgetRow> { new("A", "1", WidgetLevel.Ok, 0.1), new("B", "2", WidgetLevel.Warn, 0.2) });
                w.UpdateLayout();
                Assert.True(w.ActualWidth > 40);
            }
            finally { w.Close(); }
        });
    }

    [Fact]
    public void WidgetsPage_AddsEditsAndHidesWidgets()
    {
        RunSta(() =>
        {
            EnsureApp();
            var w = new MainWindow();
            try
            {
                Call(w, "StartWidgets");
                Call(w, "InitWidgets");
                var settings = Field<AppSettings>(w, "_settings");
                var mgr = Field<WidgetManager>(w, "_widgets");
                Assert.Empty(settings.Widgets);
                Assert.Equal(Visibility.Visible, Field<TextBlock>(w, "WEmpty").Visibility);

                CallH(w, "WidgetAdd_Click");
                Assert.Single(settings.Widgets);
                Assert.Equal(1, mgr.OpenCount);
                Assert.Single(Field<ListBox>(w, "WList").Items);
                Assert.Equal(Visibility.Visible, Field<StackPanel>(w, "WEditor").Visibility);
                Assert.Equal(WidgetCatalog.DefaultMetrics.Length, Field<ListBox>(w, "WMetricList").Items.Count);
                Assert.True(Field<ListBox>(w, "WAvail").Items.Count > 0);

                // mudar um campo reflete na configuração e na janela
                Field<CheckBox>(w, "WClickThrough").IsChecked = true;
                Assert.True(settings.Widgets[0].ClickThrough);
                Field<CheckBox>(w, "WVisible").IsChecked = false;
                Assert.Equal(0, mgr.OpenCount);
                Field<CheckBox>(w, "WVisible").IsChecked = true;
                Assert.Equal(1, mgr.OpenCount);

                // duplicar cria outro com id diferente
                CallH(w, "WidgetDup_Click");
                Assert.Equal(2, settings.Widgets.Count);
                Assert.NotEqual(settings.Widgets[0].Id, settings.Widgets[1].Id);
                Assert.Equal(2, mgr.OpenCount);
            }
            finally { w.Close(); }
        });
    }

    private static void CallH(object o, string name) =>
        o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(o, new object?[] { null, new RoutedEventArgs() });

    [Fact]
    public void Tray_HidesOnCloseOnlyWhenEnabled_AndExitReallyCloses()
    {
        Assert.True(MainWindow.ShouldHideOnClose(true, false));
        Assert.False(MainWindow.ShouldHideOnClose(false, false));
        Assert.False(MainWindow.ShouldHideOnClose(true, true));   // Sair / desligar o Windows

        RunSta(() =>
        {
            EnsureApp();
            var w = new MainWindow();
            try
            {
                Call(w, "InitTray");
                var settings = Field<AppSettings>(w, "_settings");
                var tray = Field<System.Windows.Forms.NotifyIcon>(w, "_tray");
                Assert.True(tray.Visible);

                w.Show();
                var onClosing = w.GetType().GetMethod("OnClosingToTray", BindingFlags.NonPublic | BindingFlags.Instance)!;

                settings.CloseToTray = false;
                var e1 = new System.ComponentModel.CancelEventArgs();
                onClosing.Invoke(w, new object?[] { w, e1 });
                Assert.False(e1.Cancel);                 // opção desligada: fechar é fechar

                settings.CloseToTray = true; settings.TrayHintShown = true;
                var e2 = new System.ComponentModel.CancelEventArgs();
                onClosing.Invoke(w, new object?[] { w, e2 });
                Assert.True(e2.Cancel);                  // ligada: cancela e esconde
                Assert.False(w.IsVisible);

                w.ShowFromTray();
                Assert.True(w.IsVisible);
            }
            finally
            {
                w.GetType().GetField("_exiting", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(w, true);
                w.Close();
            }
        });
    }

    [Fact]
    public void ImportedProfile_UpdatesScreenWithoutRestart()
    {
        RunSta(() =>
        {
            EnsureApp();
            var w = new MainWindow();
            try
            {
                Call(w, "StartWidgets");
                Call(w, "InitSettings");
                Call(w, "InitWidgets");
                var settings = Field<AppSettings>(w, "_settings");
                var mgr = Field<WidgetManager>(w, "_widgets");

                var src = new AppSettings { AlertTempC = 80, AlertRamPct = 85, HistoryKeepDays = 45, OverlayCorner = "BR", OverlayScale = 1.3, DiagScenario = "disco" };
                src.Widgets.Add(new WidgetConfig { Name = "Importado" });
                var sections = new[] { ProfileSection.Alerts, ProfileSection.History, ProfileSection.Widgets, ProfileSection.Overlay, ProfileSection.Diagnostic };
                var file = ProfileTransfer.Read(ProfileTransfer.Export(src, sections, false));
                ProfileTransfer.Apply(settings, file, sections);

                w.GetType().GetMethod("ApplyImportedProfile", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(w, new object[] { (IReadOnlyCollection<ProfileSection>)sections });

                Assert.Equal("80", Field<TextBox>(w, "AlertTemp").Text);
                Assert.Equal("85", Field<TextBox>(w, "AlertRam").Text);
                Assert.Equal("45", Field<TextBox>(w, "HistDays").Text);
                Assert.Equal(1, mgr.OpenCount);                                   // widget importado abriu na hora
                Assert.Single(Field<ListBox>(w, "WList").Items);
                var corner = Field<StackPanel>(w, "OvCornerPanel").Children.OfType<RadioButton>().Single(r => r.IsChecked == true);
                Assert.Equal("BR", (string)corner.Tag);
            }
            finally { w.Close(); }
        });
    }

    [Fact]
    public void GoogleCard_ReflectsConfigurationState()
    {
        RunSta(() =>
        {
            EnsureApp();
            var w = new MainWindow();
            try
            {
                var settings = Field<AppSettings>(w, "_settings");
                Call(w, "InitSettings");
                Assert.Contains("Não configurado", Field<TextBlock>(w, "GoogleStatus").Text);
                Assert.False(Field<Button>(w, "GoogleConnectBtn").IsEnabled);
                Assert.False(Field<Button>(w, "GoogleUploadBtn").IsEnabled);

                settings.GoogleClientId = "abc.apps.googleusercontent.com";
                Call(w, "InitSettings");
                Assert.Contains("Pronto para conectar", Field<TextBlock>(w, "GoogleStatus").Text);
                Assert.True(Field<Button>(w, "GoogleConnectBtn").IsEnabled);
                Assert.False(Field<Button>(w, "GoogleDisconnectBtn").IsEnabled);

                settings.GoogleRefreshToken = "rt"; settings.GoogleEmail = "a@b.com";
                Call(w, "InitSettings");
                Assert.Contains("Conectado como a@b.com", Field<TextBlock>(w, "GoogleStatus").Text);
                Assert.False(Field<Button>(w, "GoogleConnectBtn").IsEnabled);
                Assert.True(Field<Button>(w, "GoogleUploadBtn").IsEnabled);
                Assert.True(Field<Button>(w, "GoogleDisconnectBtn").IsEnabled);
            }
            finally { w.Close(); }
        });
    }
}
