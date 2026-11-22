using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PCMonitorPro;

/// <summary>Página Configurações: alertas, iniciar com o Windows, histórico, cores dos widgets, pasta dos relatórios e sobre.</summary>
public partial class MainWindow
{
    private bool _setLoading;

    /// <summary>Aplica as configurações guardadas que valem para o programa todo. Chamado quando a janela carrega.</summary>
    private void ApplyStoredSettings()
    {
        ReportStore.Custom = _settings.ReportFolder;
        WidgetFormat.ApplyLimits(_settings);
    }

    private void InitSettings()
    {
        LoadAlertUi();
        _ = LoadAutoStartUi();
        _setLoading = true;
        HistEnabled.IsChecked = _settings.HistoryEnabled;
        HistDays.Text = _settings.HistoryKeepDays.ToString();
        LimTempWarn.Text = WidgetFormat.TempWarn.ToString("0"); LimTempBad.Text = WidgetFormat.TempBad.ToString("0");
        LimPctWarn.Text = WidgetFormat.PctWarn.ToString("0"); LimPctBad.Text = WidgetFormat.PctBad.ToString("0");
        _setLoading = false;
        ShowReportFolder();
        AboutText.Text = $"PC Monitor Pro v{AppVersion}\nDados e log: {AppSettings.Folder}\n" +
                         "Baseado no RESET EXPLODE (licença MIT) e no PC Monitor. Veja o NOTICE.md para as licenças.";
    }

    private void SetHistory_Changed(object sender, RoutedEventArgs e)
    {
        if (_setLoading) return;
        _settings.HistoryEnabled = HistEnabled.IsChecked == true;
        _settings.HistoryKeepDays = int.TryParse(HistDays.Text, out var d) ? Math.Clamp(d, 1, 365) : _settings.HistoryKeepDays;
        _settings.Save();
        _setLoading = true; HistDays.Text = _settings.HistoryKeepDays.ToString(); _setLoading = false;
    }

    private void SetLimits_Changed(object sender, RoutedEventArgs e)
    {
        if (_setLoading) return;
        int Read(TextBox box, int current) => int.TryParse(box.Text, out var v) ? Math.Clamp(v, 1, 150) : current;
        int tw = Read(LimTempWarn, _settings.WidgetTempWarn), tb = Read(LimTempBad, _settings.WidgetTempBad);
        int pw = Read(LimPctWarn, _settings.WidgetPctWarn), pb = Read(LimPctBad, _settings.WidgetPctBad);
        // amarelo precisa ser menor que vermelho; senão mantém os valores anteriores
        if (tw < tb) { _settings.WidgetTempWarn = tw; _settings.WidgetTempBad = tb; }
        if (pw < pb) { _settings.WidgetPctWarn = pw; _settings.WidgetPctBad = pb; }
        _settings.Save();
        WidgetFormat.ApplyLimits(_settings);
        _setLoading = true;
        LimTempWarn.Text = _settings.WidgetTempWarn.ToString(); LimTempBad.Text = _settings.WidgetTempBad.ToString();
        LimPctWarn.Text = _settings.WidgetPctWarn.ToString(); LimPctBad.Text = _settings.WidgetPctBad.ToString();
        _setLoading = false;
    }

    private void ShowReportFolder() =>
        ReportFolderText.Text = string.IsNullOrWhiteSpace(_settings.ReportFolder)
            ? $"Pasta padrão: {ReportStore.DefaultFolder}"
            : $"Pasta escolhida: {_settings.ReportFolder}";

    private void BtnReportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta dos relatórios do diagnóstico", InitialDirectory = ReportStore.Folder };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Directory.CreateDirectory(dlg.FolderName);   // falha aqui, e não só na hora de salvar o relatório
            _settings.ReportFolder = dlg.FolderName;
            _settings.Save();
            ReportStore.Custom = _settings.ReportFolder;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Pasta de relatórios inválida", ex);
            MessageBox.Show(this, "Não foi possível usar essa pasta: " + ex.Message, "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        ShowReportFolder();
    }

    private void BtnReportDefault_Click(object sender, RoutedEventArgs e)
    {
        _settings.ReportFolder = null;
        _settings.Save();
        ReportStore.Custom = null;
        ShowReportFolder();
    }

    private void BtnReportOpen_Click(object sender, RoutedEventArgs e) => OpenFolder(ReportStore.Folder);
    private void BtnDataFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(AppSettings.Folder);

    private void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            ProcessLauncher.StartDeElevated("explorer.exe", $"\"{folder}\"");   // o Explorer não precisa de administrador
        }
        catch (Exception ex) { AppLog.Warn("Não foi possível abrir a pasta", ex); }
    }
}
