using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PCMonitorPro;

/// <summary>Página Widgets: cria e configura os widgets flutuantes (WidgetWindow) e liga o gerenciador ao relógio de 1 s.</summary>
public partial class MainWindow
{
    private WidgetManager? _widgets;
    private WidgetConfig? _wSel;
    private bool _wLoading, _wInit;
    private DispatcherTimer? _wSaveTimer;
    private static readonly string[] AccentPresets = { "#4cc9f0", "#4ade80", "#facc15", "#f87171", "#c084fc", "#fb923c", "#e8eaf0" };

    /// <summary>Abre os widgets que estavam visíveis na última vez. Chamado quando a janela principal carrega.</summary>
    private void StartWidgets()
    {
        try
        {
            _widgets = new WidgetManager(_settings);
            _widgets.Changed += () =>
            {
                QueueSaveWidgets();
                if (PageWidgets.IsVisible) LoadWidgetEditor();   // trava/oculta pelo menu do widget
            };
            _widgets.OpenSettingsRequested += OpenWidgetSettings;
            _widgets.Sync();
        }
        catch (Exception ex) { AppLog.Warn("Widgets: falha ao iniciar", ex); }
    }

    private void QueueSaveWidgets()
    {
        if (_wSaveTimer == null)
        {
            _wSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _wSaveTimer.Tick += (_, _) => { _wSaveTimer.Stop(); _settings.Save(); };
        }
        _wSaveTimer.Stop(); _wSaveTimer.Start();   // junta várias mudanças seguidas (slider) em uma gravação
    }

    private void FlushWidgets()
    {
        if (_wSaveTimer is { IsEnabled: true }) { _wSaveTimer.Stop(); _settings.Save(); }
        _widgets?.Dispose();
    }

    private void OpenWidgetSettings(string id)
    {
        _wSel = _settings.Widgets.FirstOrDefault(w => w.Id == id) ?? _wSel;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (NavWidgets.IsChecked == true) InitWidgets(); else NavWidgets.IsChecked = true;
    }

    // ---------------- página ----------------

    private void InitWidgets()
    {
        if (!_wInit)
        {
            _wInit = true;
            for (int i = 0; i < AccentPresets.Length; i++)
            {
                var hex = AccentPresets[i];
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var b = new Button
                {
                    Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 0), Tag = hex, Background = new SolidColorBrush(color),
                    BorderBrush = (Brush)FindResource("LineStrong"), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = hex, Template = RoundSwatch(),
                };
                b.Click += (_, _) => { WAccent.Text = hex; WField_Changed(b, new RoutedEventArgs()); };
                WAccents.Children.Insert(i, b);
            }
        }
        if (_widgets != null && _monitor.IsOpen) _widgets.RefreshCatalog(_monitor);
        RefreshWidgetList();
    }

    private static ControlTemplate RoundSwatch()
    {
        var t = new ControlTemplate(typeof(Button));
        var f = new FrameworkElementFactory(typeof(Border));
        f.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        f.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        f.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        f.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        t.VisualTree = f;
        return t;
    }

    private static string WidgetTitle(WidgetConfig w) => w.Visible ? w.Name : w.Name + " (oculto)";

    private void RefreshWidgetList()
    {
        _wLoading = true;
        WList.Items.Clear();
        foreach (var w in _settings.Widgets)
            WList.Items.Add(new ListBoxItem { Content = new TextBlock { Text = WidgetTitle(w), TextTrimming = TextTrimming.CharacterEllipsis }, Tag = w.Id });
        _wSel = _settings.Widgets.FirstOrDefault(w => w == _wSel) ?? _settings.Widgets.FirstOrDefault();
        if (_wSel != null) WList.SelectedIndex = _settings.Widgets.IndexOf(_wSel);
        _wLoading = false;
        LoadWidgetEditor();
    }

    private void WList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_wLoading) return;
        _wSel = WList.SelectedItem is ListBoxItem { Tag: string id } ? _settings.Widgets.FirstOrDefault(w => w.Id == id) : null;
        LoadWidgetEditor();
    }

    private void LoadWidgetEditor()
    {
        var w = _wSel;
        WEditor.Visibility = w == null ? Visibility.Collapsed : Visibility.Visible;
        WEmpty.Visibility = w == null ? Visibility.Visible : Visibility.Collapsed;
        WDup.IsEnabled = WDel.IsEnabled = w != null;
        if (w == null) return;
        _wLoading = true;
        WName.Text = w.Name;
        WVisible.IsChecked = w.Visible; WTitle.IsChecked = w.Title; WBars.IsChecked = w.Bars;
        WLocked.IsChecked = w.Locked; WClickThrough.IsChecked = w.ClickThrough;
        WLayH.IsChecked = w.Horizontal; WLayV.IsChecked = !w.Horizontal;
        WOpacity.Value = w.Opacity; WScale.Value = w.Scale;
        WAccent.Text = w.Accent;
        WOpacityLabel.Text = $"OPACIDADE · {w.Opacity:P0}";
        WScaleLabel.Text = $"TAMANHO · {w.Scale:P0}";
        _wLoading = false;
        RefreshWidgetMetrics();
    }

    /// <summary>Lê os controles para o widget escolhido e aplica na tela.</summary>
    private void WField_Changed(object sender, RoutedEventArgs e)
    {
        if (_wLoading || _wSel is not { } w) return;
        w.Name = string.IsNullOrWhiteSpace(WName.Text) ? "Widget" : WName.Text.Trim();
        w.Visible = WVisible.IsChecked == true; w.Title = WTitle.IsChecked == true; w.Bars = WBars.IsChecked == true;
        w.Locked = WLocked.IsChecked == true; w.ClickThrough = WClickThrough.IsChecked == true;
        w.Layout = WLayH.IsChecked == true ? "horizontal" : "vertical";
        w.Opacity = WOpacity.Value; w.Scale = WScale.Value;
        // cor inválida volta para a anterior, em vez de gravar lixo
        if (WidgetFormat.TryParseColor(WAccent.Text.Trim(), out _)) w.Accent = WAccent.Text.Trim();
        WOpacityLabel.Text = $"OPACIDADE · {w.Opacity:P0}";
        WScaleLabel.Text = $"TAMANHO · {w.Scale:P0}";
        ApplyWidgets();
    }

    private void ApplyWidgets()
    {
        _widgets?.Sync();
        QueueSaveWidgets();
        if (_wSel != null && WList.SelectedItem is ListBoxItem { Content: TextBlock t }) t.Text = WidgetTitle(_wSel);
    }

    private void WidgetAdd_Click(object sender, RoutedEventArgs e)
    {
        int n = _settings.Widgets.Count;
        var w = new WidgetConfig { Name = n == 0 ? "Principal" : $"Widget {n + 1}", X = 40 + 30 * n, Y = 40 + 30 * n };
        _settings.Widgets.Add(w);
        _wSel = w;
        ApplyWidgets();
        RefreshWidgetList();
    }

    private void WidgetDup_Click(object sender, RoutedEventArgs e)
    {
        if (_wSel is not { } src) return;
        var c = src.Clone();
        c.Id = Guid.NewGuid().ToString("N")[..8];
        c.Name = src.Name + " (cópia)"; c.X += 30; c.Y += 30; c.Visible = true;
        _settings.Widgets.Add(c);
        _wSel = c;
        ApplyWidgets();
        RefreshWidgetList();
    }

    private void WidgetDel_Click(object sender, RoutedEventArgs e)
    {
        if (_wSel is not { } w) return;
        if (MessageBox.Show(this, $"Remover o widget \"{w.Name}\"?", "Widgets", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _settings.Widgets.Remove(w);
        _wSel = null;
        ApplyWidgets();
        RefreshWidgetList();
    }

    private void WCorner_Click(object sender, RoutedEventArgs e)
    {
        if (_wSel is not { } w || sender is not Button { Tag: string tag }) return;
        if (!w.Visible) { w.Visible = true; WVisible.IsChecked = true; ApplyWidgets(); }
        _widgets?.PlaceInCorner(w.Id, left: tag[0] == 'l', top: tag[1] == 't');
    }

    // ---------------- o que mostrar ----------------

    private void RefreshWidgetMetrics(int select = -1)
    {
        WMetricList.Items.Clear();
        if (_wSel is { } w)
            foreach (var id in w.Metrics)
                WMetricList.Items.Add(new ListBoxItem { Content = _widgets?.Find(id)?.Label ?? WidgetCatalog.Base.FirstOrDefault(m => m.Id == id)?.Label ?? id + " (indisponível)", Tag = id });
        if (select >= 0 && WMetricList.Items.Count > 0) WMetricList.SelectedIndex = Math.Min(select, WMetricList.Items.Count - 1);
        RefreshWidgetAvail();
    }

    private void RefreshWidgetAvail()
    {
        WAvail.Items.Clear();
        var have = _wSel?.Metrics.ToHashSet() ?? new HashSet<string>();
        var text = WFilter.Text.Trim();
        var list = (IEnumerable<WidgetMetric>)(_widgets?.Catalog ?? WidgetCatalog.Base);
        foreach (var m in list)
        {
            if (have.Contains(m.Id)) continue;
            if (text != "" && !m.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase) && !m.Group.Contains(text, StringComparison.CurrentCultureIgnoreCase)) continue;
            WAvail.Items.Add(new ListBoxItem { Content = m.Label, Tag = m.Id });
        }
        if (WAvail.Items.Count == 0)
            WAvail.Items.Add(new ListBoxItem { Content = _monitor.IsOpen ? "Nada encontrado." : "Os sensores ainda estão sendo detectados…", IsEnabled = false, Focusable = false });
    }

    private void WFilter_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded || _wInit) RefreshWidgetAvail(); }

    private void AddMetrics(IEnumerable<string> ids)
    {
        if (_wSel is not { } w) return;
        foreach (var id in ids) if (!w.Metrics.Contains(id)) w.Metrics.Add(id);
        ApplyWidgets();
        RefreshWidgetMetrics(w.Metrics.Count - 1);
    }

    private void WMetricAdd_Click(object sender, RoutedEventArgs e) =>
        AddMetrics(WAvail.SelectedItems.OfType<ListBoxItem>().Where(i => i.Tag is string).Select(i => (string)i.Tag));

    private void WAvail_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (WAvail.SelectedItem is ListBoxItem { Tag: string id }) AddMetrics(new[] { id });
    }

    private void WMetricRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_wSel is not { } w || WMetricList.SelectedIndex < 0) return;
        int i = WMetricList.SelectedIndex;
        w.Metrics.RemoveAt(i);
        ApplyWidgets();
        RefreshWidgetMetrics(i);
    }

    private void WMetricUp_Click(object sender, RoutedEventArgs e) => MoveMetric(-1);
    private void WMetricDown_Click(object sender, RoutedEventArgs e) => MoveMetric(1);

    private void MoveMetric(int delta)
    {
        if (_wSel is not { } w) return;
        int i = WMetricList.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= w.Metrics.Count) return;
        (w.Metrics[i], w.Metrics[j]) = (w.Metrics[j], w.Metrics[i]);
        ApplyWidgets();
        RefreshWidgetMetrics(j);
    }
}
