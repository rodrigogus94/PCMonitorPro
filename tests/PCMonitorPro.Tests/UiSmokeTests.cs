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

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            // A janela dispara tarefas assíncronas no construtor; as continuações precisam voltar para a thread de UI.
            var disp = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(disp));
            disp.BeginInvoke(new Action(() =>
            {
                try { body(); } catch (Exception ex) { error = ex; }
                // dá tempo das continuações pendentes rodarem antes de encerrar
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                timer.Tick += (_, _) => { timer.Stop(); disp.InvokeShutdown(); };
                timer.Start();
            }));
            System.Windows.Threading.Dispatcher.Run();
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(60)), "a janela travou");
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
            if (Application.Current == null)
            {
                // URIs de pacote relativas (Assets/logo.png) precisam apontar para o assembly do programa, não para o testhost
                typeof(Application).GetField("_resourceAssembly", BindingFlags.NonPublic | BindingFlags.Static)!
                    .SetValue(null, typeof(MainWindow).Assembly);
                var app = new App();
                app.InitializeComponent();
            }
            var w = new MainWindow();
            try
            {
                Call(w, "InitDiag");
                var chips = Field<WrapPanel>(w, "DiagScenarios");
                Assert.Equal(Diagnostics.Scenarios.Count, chips.Children.Count);
                Assert.Contains("Nenhum marco", Field<TextBlock>(w, "DiagMilestoneText").Text);

                Assert.True(Field<CheckBox>(w, "AlertEnabled").IsChecked);
                Assert.Equal("90", Field<TextBox>(w, "AlertTemp").Text);
                Assert.Equal("95", Field<TextBox>(w, "AlertRam").Text);

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
}
