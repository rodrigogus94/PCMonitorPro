using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PCMonitorPro;

/// <summary>Páginas Diagnóstico (coleta + análise do Windows, marco de comparação) e Comandos sugeridos; gravação do histórico de sensores.</summary>
public partial class MainWindow
{
    private SessionRecorder? _recorder;
    private string? _crashText;
    private DiagnosticReport? _diagReport;
    private bool _diagBusy, _diagInit;
    private CancellationTokenSource? _diagCts;
    private string _diagScenario = "geral";

    // ================= Histórico de sensores (queda = sessão sem fim normal) =================

    /// <summary>Abre a sessão de histórico e avisa se a anterior terminou de forma inesperada. Chamado quando os sensores abrem.</summary>
    private async void StartHistory()
    {
        try
        {
            if (!_settings.HistoryEnabled) return;
            _recorder = new SessionRecorder(keepDays: _settings.HistoryKeepDays);
            await Task.Run(_recorder.Start);
            if (_recorder.PreviousUnclean is not { } prev) return;
            string detail = await Task.Run(() =>
            {
                try { return HistoryLogger.DescribeLast(HistoryLogger.Summarize(prev.Path)); }
                catch (Exception ex) { AppLog.Debug(ex); return ""; }
            });
            _crashText = $"A sessão iniciada em {prev.Start:dd/MM HH:mm} parou de gravar às {prev.Last}, sem o fim normal. " +
                         "Pode ter sido tela azul, queda de energia, reinício seco ou o programa fechado à força." +
                         (detail != "" ? $"\nÚltimos valores gravados: {detail}." : "");
            Toast("Sessão anterior terminou de forma inesperada", $"Parou às {prev.Last}. Abra Diagnóstico para investigar.", "");
            ShowCrashBanner();
        }
        catch (Exception ex) { AppLog.Warn("Histórico: falha ao iniciar", ex); }
    }

    private void ShowCrashBanner()
    {
        DiagCrashBanner.Visibility = _crashText == null ? Visibility.Collapsed : Visibility.Visible;
        DiagCrashText.Text = _crashText ?? "";
    }

    // ================= Diagnóstico =================

    private void InitDiag()
    {
        if (!_diagInit)
        {
            _diagInit = true;
            _diagScenario = Diagnostics.Scenarios.ContainsKey(_settings.DiagScenario) ? _settings.DiagScenario : "geral";
            foreach (var (key, sc) in Diagnostics.Scenarios)
            {
                var chip = new RadioButton { Content = sc.Name, GroupName = "DiagScn", Tag = key, Style = (Style)FindResource("Chip"), IsChecked = key == _diagScenario };
                chip.Checked += (_, _) =>
                {
                    _diagScenario = (string)chip.Tag;
                    _settings.DiagScenario = _diagScenario;
                    _settings.Save();
                };
                DiagScenarios.Children.Add(chip);
            }
        }
        ShowCrashBanner();
        ShowMilestone();
    }

    // ================= Iniciar com o Windows =================

    private bool _autoLoading;

    private async Task LoadAutoStartUi()
    {
        try
        {
            var st = await Task.Run(AutoStart.GetState);
            _autoLoading = true;
            AutoStartCheck.IsChecked = st.Exists;
            _autoLoading = false;
            AutoStartStatus.Text = !st.Exists ? "" : st.IsThisExe ? "Ativado."
                : $"Ativado, mas aponta para outro arquivo ({st.Command}). Desmarque e marque de novo para apontar para este.";
        }
        catch (Exception ex) { _autoLoading = false; AppLog.Debug(ex); }
    }

    private async void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_autoLoading) return;
        bool want = AutoStartCheck.IsChecked == true;
        AutoStartCheck.IsEnabled = false;
        AutoStartStatus.Text = "Aplicando…";
        try
        {
            var (ok, msg) = await Task.Run(() => want ? AutoStart.Enable() : AutoStart.Disable());
            AutoStartStatus.Text = msg;
            if (!ok)   // volta a caixa ao estado real, sem disparar de novo
            {
                _autoLoading = true;
                AutoStartCheck.IsChecked = !want;
                _autoLoading = false;
            }
        }
        catch (Exception ex) { AppLog.Warn("Autostart", ex); AutoStartStatus.Text = "Falha: " + ex.Message; }
        finally { AutoStartCheck.IsEnabled = true; }
    }

    // ================= Alertas de temperatura e RAM =================

    private readonly AlertMonitor _alerts = new();
    private bool _alertLoading;

    private void LoadAlertUi()
    {
        _alertLoading = true;
        AlertEnabled.IsChecked = _settings.AlertsEnabled;
        AlertTemp.Text = _settings.AlertTempC.ToString();
        AlertRam.Text = _settings.AlertRamPct.ToString();
        _alertLoading = false;
    }

    private void AlertSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_alertLoading) return;
        _settings.AlertsEnabled = AlertEnabled.IsChecked == true;
        // valor inválido volta para o último bom, em vez de gravar lixo
        _settings.AlertTempC = int.TryParse(AlertTemp.Text, out var t) ? Math.Clamp(t, 50, 120) : _settings.AlertTempC;
        _settings.AlertRamPct = int.TryParse(AlertRam.Text, out var r) ? Math.Clamp(r, 50, 100) : _settings.AlertRamPct;
        _settings.Save();
        LoadAlertUi();
    }

    /// <summary>Chamado a cada segundo: avisa no canto da tela se algum limite foi passado (respeita o cooldown).</summary>
    private void CheckAlerts()
    {
        if (!_settings.AlertsEnabled || !_monitor.IsOpen || AnyBenchRunning) return;
        try
        {
            var lines = _alerts.Evaluate(_settings, AlertMonitor.TempsFrom(_monitor), HistorySampler.RamPercent(_monitor), DateTime.Now);
            if (lines.Count > 0) Toast("Alerta do PC", AlertMonitor.Format(lines), "");
        }
        catch (Exception ex) { AppLog.Debug(ex); }
    }

    private void ShowMilestone()
    {
        DiagMilestoneText.Text = _settings.MilestoneTime is { } t
            ? $"Marco atual: {(string.IsNullOrWhiteSpace(_settings.MilestoneNote) ? "mudança" : _settings.MilestoneNote)} em {t:dd/MM/yyyy HH:mm}. " +
              "O diagnóstico compara as quedas antes e depois dele."
            : "Nenhum marco definido. Marque depois de uma mudança (EXPO desativado, BIOS nova, driver…) para ver se as quedas diminuíram.";
    }

    private void BtnDiagMark_Click(object sender, RoutedEventArgs e)
    {
        _settings.MilestoneTime = DateTime.Now;
        _settings.MilestoneNote = DiagNote.Text.Trim();
        _settings.Save();
        DiagNote.Text = "";
        ShowMilestone();
    }

    private void BtnDiagUnmark_Click(object sender, RoutedEventArgs e)
    {
        _settings.MilestoneTime = null;
        _settings.MilestoneNote = "";
        _settings.Save();
        ShowMilestone();
    }

    private async void BtnDiagRun_Click(object sender, RoutedEventArgs e)
    {
        if (_diagBusy) { _diagCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { DiagStatus.Text = "Termine o outro teste antes."; return; }

        _diagBusy = true;
        _diagCts = new CancellationTokenSource();
        var ct = _diagCts.Token;
        BtnDiagRun.Content = "CANCELAR";
        DiagStatus.Text = "Coletando informações do Windows… (de 15 a 60 segundos)";
        string scenario = _diagScenario;
        var milestone = _settings.MilestoneTime is { } t ? new Milestone(t, _settings.MilestoneNote) : null;
        string folder = _recorder?.Folder ?? SessionRecorder.DefaultFolder;
        string? current = _recorder?.CurrentPath;
        try
        {
            var raw = await DiagnosticsCollector.CollectAsync(ct);
            DiagStatus.Text = "Analisando…";
            var rep = await Task.Run(() =>
            {
                var hist = HistoryLogger.UncleanLastValues(folder, exclude: current);
                var recent = HistoryLogger.ReadRecentStats(folder, 24);
                return DiagnosticsAnalyzer.Analyze(raw, hist, milestone, DateTime.Now, scenario, recent);
            }, ct);
            _diagReport = rep;
            string text = rep.ToText();
            DiagReport.Text = text;
            BtnDiagCopy.IsEnabled = BtnDiagSave.IsEnabled = true;
            string top = rep.Top is { } h ? $"Causa mais provável: {h.Name} (confiança {rep.Confidence})." : "Nenhuma causa provável encontrada para este foco.";
            string saved = "";
            try { saved = " Salvo em " + ReportStore.Save(text); }
            catch (Exception ex) { AppLog.Warn("Diagnóstico: não foi possível salvar o relatório", ex); }
            DiagStatus.Text = top + saved;
            if (PageCmds.IsVisible) RefreshCommands();
        }
        catch (OperationCanceledException) { DiagnosticsCollector.Cancel(); DiagStatus.Text = "Cancelado."; }
        catch (Exception ex)
        {
            AppLog.Error("Diagnóstico falhou", ex);
            DiagStatus.Text = "Não foi possível diagnosticar: " + ex.Message;
        }
        finally
        {
            _diagBusy = false;
            BtnDiagRun.Content = "ANALISAR";
        }
    }

    private void BtnDiagCopy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(DiagReport.Text); DiagStatus.Text = "Relatório copiado."; }
        catch (Exception ex) { AppLog.Warn("Área de transferência ocupada", ex); DiagStatus.Text = "Não foi possível copiar (área de transferência em uso)."; }
    }

    private void BtnDiagSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = ReportStore.Save(DiagReport.Text);
            DiagStatus.Text = "Salvo em " + path;
            ProcessLauncher.StartDeElevated("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex) { AppLog.Warn("Diagnóstico: falha ao salvar", ex); DiagStatus.Text = "Não foi possível salvar: " + ex.Message; }
    }

    private void BtnDiagHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = _recorder?.Folder ?? SessionRecorder.DefaultFolder;
            Directory.CreateDirectory(folder);
            ProcessLauncher.StartDeElevated("explorer.exe", $"\"{folder}\"");
        }
        catch (Exception ex) { AppLog.Warn("Histórico: falha ao abrir a pasta", ex); }
    }

    private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(AppLog.FilePath)) { DiagStatus.Text = "Ainda não há log: nada deu errado até agora."; return; }
            ProcessLauncher.StartDeElevated("notepad.exe", $"\"{AppLog.FilePath}\"");
        }
        catch (Exception ex) { AppLog.Warn("Log: falha ao abrir", ex); }
    }

    // ================= Comandos sugeridos =================

    private List<CommandSuggestion> _cmdItems = new();
    private CancellationTokenSource? _cmdCts;

    private void CmdShowAll_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshCommands(); }

    private void RefreshCommands()
    {
        var focus = Diagnostics.Scenarios.TryGetValue(_diagScenario, out var sc) ? sc.Focus : null;
        var hyp = new HashSet<string>();
        if (_diagReport != null)
            foreach (var h in _diagReport.Hypotheses.Take(2).Where(h => h.Score > 0)) hyp.UnionWith(Diagnostics.HypTags[h.Key]);
        _cmdItems = CommandSuggestions.Suggest(focus, hyp.Count > 0 ? hyp : null, CmdShowAll.IsChecked == true);
        CmdContext.Text = $"Foco: {(sc ?? Diagnostics.Scenarios["geral"]).Name}" +
                          (hyp.Count > 0 ? " — ordenados pelas causas prováveis do último diagnóstico" : "");

        CmdList.Items.Clear();
        string? last = null; int select = -1;
        for (int i = 0; i < _cmdItems.Count; i++)
        {
            var (c, group) = (_cmdItems[i].Command, _cmdItems[i].Group);
            if (group != last)
            {
                last = group;
                CmdList.Items.Add(new ListBoxItem { Content = new TextBlock { Text = $"──  {group}  ──", FontSize = 11 }, IsEnabled = false, Focusable = false });
            }
            var item = new ListBoxItem { Content = new TextBlock { Text = (c.Alters ? "⚠  " : "🔍  ") + c.Title, TextWrapping = TextWrapping.Wrap }, Tag = i };
            CmdList.Items.Add(item);
            if (select < 0) select = CmdList.Items.Count - 1;
        }
        if (select >= 0) CmdList.SelectedIndex = select;
    }

    private SuggestedCommand? CurrentCommand =>
        CmdList.SelectedItem is ListBoxItem { Tag: int i } && i >= 0 && i < _cmdItems.Count ? _cmdItems[i].Command : null;

    private void CmdList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CurrentCommand is not { } c) return;
        CmdTitle.Text = c.Title;
        CmdDesc.Text = c.Desc;
        CmdBadge.Text = (c.Alters ? "⚠ Altera o sistema ou inicia reparo/verificação." : "🔍 Só consulta, não altera nada.")
                        + (c.Admin ? " Exige administrador." : "");
        CmdText.Text = c.Command;
    }

    private void BtnCmdCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(CmdText.Text);
            CmdOutput.AppendText("— comando copiado. Cole no PowerShell (os que exigem administrador pedem um PowerShell aberto como administrador).\n");
        }
        catch (Exception ex) { AppLog.Warn("Área de transferência ocupada", ex); }
    }

    private async void BtnCmdRun_Click(object sender, RoutedEventArgs e)
    {
        var c = CurrentCommand;
        string text = CmdText.Text.Trim();
        if (c == null || text == "" || CommandRunner.Running) return;
        if (c.Admin && !SensorMonitor.IsElevated)
        {
            MessageBox.Show(this, "Este comando exige administrador. Feche e abra o PC Monitor Pro aceitando o pedido de administrador, " +
                                  "ou copie o comando e rode num PowerShell aberto como administrador.", "Administrador necessário",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var ask = MessageBox.Show(this,
            $"{c.Title}\n\nEste comando será executado agora no PowerShell, só porque você escolheu.\n\n" +
            (c.Alters ? "Ele ALTERA o sistema ou inicia um reparo. Confirme apenas se entendeu a descrição.\n\n" : "É um comando de consulta: apenas lê informações.\n\n") + text,
            "Executar comando?", MessageBoxButton.OKCancel, c.Alters ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (ask != MessageBoxResult.OK) return;

        CmdOutput.AppendText($"\nPS> {text}\n\n");
        BtnCmdRun.IsEnabled = false; BtnCmdStop.IsEnabled = true;
        _cmdCts = new CancellationTokenSource();
        try
        {
            int code = await CommandRunner.RunAsync(text, s => Dispatcher.BeginInvoke(new Action(() =>
            {
                CmdOutput.AppendText(s);
                if (CmdOutput.Text.Length > 300_000) CmdOutput.Text = CmdOutput.Text[^200_000..];
                CmdOutput.ScrollToEnd();
            })), _cmdCts.Token);
            CmdOutput.AppendText($"\n— terminou (código {code}).\n");
        }
        catch (OperationCanceledException) { CmdOutput.AppendText("\n— cancelado.\n"); }
        catch (Exception ex) { AppLog.Error("Comando falhou", ex); CmdOutput.AppendText($"\n— falhou: {ex.Message}\n"); }
        finally { BtnCmdRun.IsEnabled = true; BtnCmdStop.IsEnabled = false; }
    }

    private void BtnCmdStop_Click(object sender, RoutedEventArgs e) => _cmdCts?.Cancel();
}
