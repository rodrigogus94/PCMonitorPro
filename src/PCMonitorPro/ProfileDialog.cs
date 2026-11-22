using System.Windows;
using System.Windows.Controls;

namespace PCMonitorPro;

/// <summary>Janela simples para escolher quais partes do perfil exportar ou importar (marcadas por padrão).</summary>
public sealed class ProfileDialog : Window
{
    private readonly List<(ProfileSection Section, CheckBox Box)> _boxes = new();
    private readonly CheckBox? _extra;

    public List<ProfileSection> Chosen => _boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Section).ToList();
    public bool ExtraChecked => _extra?.IsChecked == true;

    private ProfileDialog(string title, string intro, IEnumerable<ProfileSection> sections, string confirmText, string? extraText, bool extraDefault)
    {
        Title = title;
        Width = 520; SizeToContent = SizeToContent.Height; MaxHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Fg");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"); FontSize = 13;

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var introText = new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        introText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        root.Children.Add(introText);

        foreach (var s in sections)
        {
            var cb = new CheckBox { Content = ProfileTransfer.Names[s], IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
            _boxes.Add((s, cb));
            root.Children.Add(cb);
        }
        if (extraText != null)
        {
            _extra = new CheckBox { Content = extraText, IsChecked = extraDefault, Margin = new Thickness(0, 10, 0, 0) };
            root.Children.Add(_extra);
        }

        var ok = new Button { Content = confirmText, Padding = new Thickness(20, 9, 20, 9), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "CANCELAR", Padding = new Thickness(20, 9, 20, 9), IsCancel = true };
        if (TryFindResource("PrimaryButton") is Style pb) ok.Style = pb;
        if (TryFindResource("OutlineButton") is Style ob) cancel.Style = ob;
        ok.Click += (_, _) =>
        {
            if (_boxes.Count > 0 && Chosen.Count == 0) { MessageBox.Show(this, "Marque pelo menos uma parte.", Title, MessageBoxButton.OK, MessageBoxImage.Information); return; }
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;
    }

    /// <summary>Mostra a janela. Devolve null se o usuário cancelou.</summary>
    public static (List<ProfileSection> Sections, bool Extra)? Ask(Window? owner, string title, string intro, IEnumerable<ProfileSection> sections,
        string confirmText, string? extraText = null, bool extraDefault = false)
    {
        var d = new ProfileDialog(title, intro, sections, confirmText, extraText, extraDefault) { Owner = owner };
        return d.ShowDialog() == true ? (d.Chosen, d.ExtraChecked) : null;
    }
}

/// <summary>Janela simples para escolher um item de uma lista (por exemplo, o computador cujo perfil baixar).</summary>
public static class ChoiceDialog
{
    /// <summary>Índice escolhido, ou null se cancelou.</summary>
    public static int? Ask(Window? owner, string title, string intro, IReadOnlyList<string> items)
    {
        var win = new Window
        {
            Title = title, Width = 520, SizeToContent = SizeToContent.Height, MaxHeight = 560, Owner = owner, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = 13,
        };
        win.SetResourceReference(Window.BackgroundProperty, "Bg"); win.SetResourceReference(Window.ForegroundProperty, "Fg");
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var text = new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var list = new ListBox { MaxHeight = 300 };
        foreach (var i in items) list.Items.Add(i);
        list.SelectedIndex = 0;
        var ok = new Button { Content = "ESCOLHER", Padding = new Thickness(20, 9, 20, 9), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "CANCELAR", Padding = new Thickness(20, 9, 20, 9), IsCancel = true };
        if (win.TryFindResource("PrimaryButton") is Style pb) ok.Style = pb;
        if (win.TryFindResource("OutlineButton") is Style ob) cancel.Style = ob;
        ok.Click += (_, _) => { if (list.SelectedIndex >= 0) win.DialogResult = true; };
        list.MouseDoubleClick += (_, _) => { if (list.SelectedIndex >= 0) win.DialogResult = true; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        root.Children.Add(text); root.Children.Add(list); root.Children.Add(buttons);
        win.Content = root;
        return win.ShowDialog() == true ? list.SelectedIndex : null;
    }
}
