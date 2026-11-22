using System.ComponentModel;
using System.Windows;

namespace PCMonitorPro;

/// <summary>Ícone da bandeja: abrir, sair e (opcional) continuar rodando quando a janela é fechada.</summary>
public partial class MainWindow
{
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _exiting;

    /// <summary>Fechar a janela só a esconde quando a opção está ligada e o programa não está sendo encerrado de verdade.</summary>
    public static bool ShouldHideOnClose(bool closeToTray, bool exiting) => closeToTray && !exiting;

    /// <summary>Aberto pelo início automático (argumento --bandeja) com a opção de bandeja ligada: começa escondido.</summary>
    private bool StartInTray =>
        _settings.CloseToTray && Environment.GetCommandLineArgs().Skip(1).Contains(AutoStart.TrayArgument, StringComparer.OrdinalIgnoreCase);

    private void InitTray()
    {
        if (_tray != null) return;
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
            var menu = new System.Windows.Forms.ContextMenuStrip();
            var open = new System.Windows.Forms.ToolStripMenuItem("Abrir PC Monitor Pro") { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) };
            open.Click += (_, _) => ShowFromTray();
            var exit = new System.Windows.Forms.ToolStripMenuItem("Sair");
            exit.Click += (_, _) => ExitApp();
            menu.Items.Add(open);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(exit);
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(stream), Text = "PC Monitor Pro", ContextMenuStrip = menu, Visible = true,
            };
            _tray.DoubleClick += (_, _) => ShowFromTray();
            Closing += OnClosingToTray;
            // desligar/reiniciar o Windows não pode ser barrado pela opção de bandeja
            if (Application.Current != null) Application.Current.SessionEnding += (_, _) => _exiting = true;
        }
        catch (Exception ex) { AppLog.Warn("Bandeja: não foi possível criar o ícone", ex); _tray = null; }
    }

    private void OnClosingToTray(object? sender, CancelEventArgs e)
    {
        if (_tray == null || !ShouldHideOnClose(_settings.CloseToTray, _exiting)) return;
        e.Cancel = true;
        Hide();
        if (!_settings.TrayHintShown)
        {
            _settings.TrayHintShown = true;
            _settings.Save();
            _tray.ShowBalloonTip(4000, "PC Monitor Pro continua aberto",
                "Widgets, histórico e alertas seguem rodando. Para sair, clique com o botão direito no ícone e escolha Sair.",
                System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    /// <summary>Traz a janela de volta (da bandeja, minimizada ou segunda abertura do programa).</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void DisposeTray()
    {
        if (_tray == null) return;
        _tray.Visible = false;   // senão o ícone fica "fantasma" na bandeja até o mouse passar por cima
        _tray.Icon?.Dispose();
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _tray = null;
    }
}
