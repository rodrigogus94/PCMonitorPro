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
        CloseToTrayCheck.IsChecked = _settings.CloseToTray;
        HistEnabled.IsChecked = _settings.HistoryEnabled;
        HistDays.Text = _settings.HistoryKeepDays.ToString();
        LimTempWarn.Text = WidgetFormat.TempWarn.ToString("0"); LimTempBad.Text = WidgetFormat.TempBad.ToString("0");
        LimPctWarn.Text = WidgetFormat.PctWarn.ToString("0"); LimPctBad.Text = WidgetFormat.PctBad.ToString("0");
        _setLoading = false;
        ShowReportFolder();
        AboutText.Text = $"PC Monitor Pro v{AppVersion}\nDados e log: {AppSettings.Folder}\n" +
                         "Baseado no RESET EXPLODE (licença MIT) e no PC Monitor. Veja o NOTICE.md para as licenças.";
    }

    private void SetTray_Changed(object sender, RoutedEventArgs e)
    {
        if (_setLoading) return;
        _settings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        _settings.Save();
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

    // ---------------- perfil (exportar / importar) ----------------

    private void BtnProfileExport_Click(object sender, RoutedEventArgs e)
    {
        var ask = ProfileDialog.Ask(this, "Exportar perfil", "Escolha o que vai para o arquivo. Chave da API, backups do Windows e perfis dos jogos nunca são incluídos.",
            Enum.GetValues<ProfileSection>(), "EXPORTAR", "Incluir o nome deste computador no arquivo", extraDefault: true);
        if (ask is not { } a) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar perfil", FileName = $"perfil-pcmonitorpro-{DateTime.Now:yyyyMMdd}", DefaultExt = ProfileTransfer.Extension,
            Filter = $"Perfil do PC Monitor Pro (*{ProfileTransfer.Extension})|*{ProfileTransfer.Extension}",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, ProfileTransfer.Export(_settings, a.Sections, a.Extra), new System.Text.UTF8Encoding(false));
            ProfileStatus.Text = $"Perfil salvo em {dlg.FileName}";
        }
        catch (Exception ex)
        {
            AppLog.Warn("Exportar perfil", ex);
            ProfileStatus.Text = "Não foi possível salvar o perfil: " + ex.Message;
        }
    }

    private void BtnProfileImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar perfil", CheckFileExists = true,
            Filter = $"Perfil do PC Monitor Pro (*{ProfileTransfer.Extension})|*{ProfileTransfer.Extension}|Todos os arquivos (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        ProfileFile file;
        try
        {
            if (new FileInfo(dlg.FileName).Length > ProfileTransfer.MaxBytes)
                throw new ProfileException("O arquivo é grande demais para ser um perfil do PC Monitor Pro.");
            file = ProfileTransfer.Read(File.ReadAllText(dlg.FileName));
        }
        catch (ProfileException ex) { ProfileStatus.Text = ex.Message; return; }
        catch (Exception ex)
        {
            AppLog.Warn("Importar perfil", ex);
            ProfileStatus.Text = "Não foi possível ler o arquivo: " + ex.Message;
            return;
        }
        var avail = ProfileTransfer.Available(file);
        if (avail.Count == 0) { ProfileStatus.Text = "Este perfil não tem nenhuma parte para importar."; return; }

        string origin = $"Perfil exportado em {file.ExportedAt:dd/MM/yyyy HH:mm}" + (string.IsNullOrWhiteSpace(file.Computer) ? "" : $" de {file.Computer}") +
                        (string.IsNullOrWhiteSpace(file.App) ? "." : $" ({file.App}).");
        var ask = ProfileDialog.Ask(this, "Importar perfil", origin + " O que você marcar substitui o que há hoje; resultados de benchmark são somados aos atuais.", avail, "IMPORTAR");
        if (ask is not { } a) return;

        ProfileTransfer.Apply(_settings, file, a.Sections);
        _settings.Save();
        ApplyImportedProfile(a.Sections);
        ProfileStatus.Text = "Perfil importado: " + string.Join(", ", a.Sections.Select(x => ProfileTransfer.Names[x].Split(" (")[0].ToLowerInvariant())) + ".";
    }

    /// <summary>Faz as partes importadas valerem agora, sem precisar reabrir o programa.</summary>
    private void ApplyImportedProfile(IReadOnlyCollection<ProfileSection> sections)
    {
        ApplyStoredSettings();
        if (sections.Contains(ProfileSection.Appearance)) { ThemeManager.Apply(_settings.LightTheme); UpdateThemeButton(); }
        if (sections.Contains(ProfileSection.Widgets)) { _widgets?.Sync(); if (_wInit) RefreshWidgetList(); }
        if (sections.Contains(ProfileSection.Overlay))
        {
            _ovShow = null;
            foreach (var rb in OvCornerPanel.Children.OfType<RadioButton>()) rb.IsChecked = (string)rb.Tag == _settings.OverlayCorner;
            foreach (var rb in OvScalePanel.Children.OfType<RadioButton>())
                rb.IsChecked = Math.Abs(double.Parse((string)rb.Tag, System.Globalization.CultureInfo.InvariantCulture) - _settings.OverlayScale) < 0.01;
            BuildOverlayPicker();
        }
        if (sections.Contains(ProfileSection.Diagnostic) && _diagInit)
            foreach (var chip in DiagScenarios.Children.OfType<RadioButton>()) chip.IsChecked = (string)chip.Tag == _settings.DiagScenario;
        if (sections.Contains(ProfileSection.Benchmarks)) UpdateDashboard();
        InitSettings();   // recarrega alertas, histórico, bandeja e limites na tela
    }
}
