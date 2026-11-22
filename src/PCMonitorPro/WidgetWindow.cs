using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PCMonitorPro;

/// <summary>
/// Widget flutuante: janela sem borda, sempre no topo, que não rouba o foco, arrastável com cantos magnéticos e, se quiser,
/// com o clique atravessando. Toda a posição é em pixels da tela virtual (mesmas unidades dos monitores).
/// </summary>
public sealed class WidgetWindow : Window
{
    private static readonly Brush TextBrush = Frozen("#e8eaf0"), MutedBrush = Frozen("#9aa3b5");
    private static readonly Brush OkBrush = Frozen("#4ade80"), WarnBrush = Frozen("#facc15"), BadBrush = Frozen("#f87171");
    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
    private const string FontName = "Segoe UI";

    private static Brush Frozen(string hex) { var b = (Brush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    private sealed record Cell(TextBlock Label, TextBlock Value, ColumnDefinition? Fill, ColumnDefinition? Rest, Border? BarFill);

    private readonly Border _box = new() { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(10) };
    private readonly List<Cell> _cells = new();
    private IntPtr _hwnd;
    private bool _dragging;
    private (int X, int Y) _dragStartCursor, _dragStartWin;
    private int _pendingX, _pendingY;
    private bool _placed;

    public WidgetConfig Config { get; private set; }

    /// <summary>Disparado quando a posição mudou por arrasto ou canto (Config.X/Y já estão atualizados).</summary>
    public event Action<WidgetWindow>? Moved;
    public event Action<WidgetWindow>? OpenSettingsRequested;
    public event Action<WidgetWindow>? HideRequested;
    public event Action<WidgetWindow>? LockChanged;

    public WidgetWindow(WidgetConfig cfg)
    {
        Config = cfg;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight; UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Title = cfg.Name;
        Content = _box;
        _pendingX = cfg.X; _pendingY = cfg.Y;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyStyles();
            MovePx(_pendingX, _pendingY);
        };
        ContentRendered += (_, _) => { if (!_placed) { _placed = true; EnsureOnScreen(); } };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        ContextMenuOpening += (_, _) => BuildMenu();
        Configure(cfg);
    }

    // ------------------------------------------------------------------ configuração / conteúdo

    public void Configure(WidgetConfig cfg)
    {
        Config = cfg;
        Title = cfg.Name;
        var (r, g, b) = WidgetFormat.TryParseColor(cfg.Accent, out var c) ? c : ((byte)0x4c, (byte)0xc9, (byte)0xf0);
        _accent = new SolidColorBrush(Color.FromRgb(r, g, b)); _accent.Freeze();
        _box.Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(cfg.Opacity, 0.2, 1) * 255), 16, 18, 24));
        _box.BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        _box.LayoutTransform = new ScaleTransform(cfg.Scale, cfg.Scale);
        _box.MinWidth = 120;
        // até a primeira leitura, mostra as linhas escolhidas com "—"
        _rows = cfg.Metrics.Select(id => new WidgetRow(id, "—", WidgetLevel.Neutral, null)).ToList();
        Rebuild();
        ApplyStyles();
    }

    private Brush _accent = Brushes.DeepSkyBlue;
    private List<WidgetRow> _rows = new();

    /// <summary>Atualiza os textos, cores e barras. Reconstrói só se o número de linhas mudou.</summary>
    public void SetRows(List<WidgetRow> rows)
    {
        bool rebuild = rows.Count != _cells.Count;
        _rows = rows;
        if (rebuild) Rebuild(); else Fill();
        // a largura só cresce, para o widget não tremer quando os números mudam de tamanho
        if (IsLoaded) { UpdateLayout(); if (_box.ActualWidth > _box.MinWidth) _box.MinWidth = _box.ActualWidth; }
    }

    private static TextBlock Tb(double size, Brush fg, bool bold = false) => new()
    {
        FontFamily = new FontFamily(FontName), FontSize = size, Foreground = fg,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Barra de 3 px: a coluna "fill" cresce com o valor e a "rest" ocupa o resto.</summary>
    private (Grid Bar, ColumnDefinition Fill, ColumnDefinition Others, Border Fg) NewBar()
    {
        var fill = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        var rest = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        var bar = new Grid { Height = 3, Margin = new Thickness(0, 1, 0, 4) };
        bar.ColumnDefinitions.Add(fill); bar.ColumnDefinitions.Add(rest);
        var track = new Border { Background = TrackBrush, CornerRadius = new CornerRadius(1.5) };
        Grid.SetColumnSpan(track, 2);
        var fg = new Border { CornerRadius = new CornerRadius(1.5), Background = TextBrush };
        Grid.SetColumn(fg, 0);
        bar.Children.Add(track); bar.Children.Add(fg);
        return (bar, fill, rest, fg);
    }

    private void Rebuild()
    {
        _cells.Clear();
        var root = new StackPanel();
        if (Config.Title)
        {
            root.Children.Add(Tb(11, _accent, bold: true).With(t => t.Text = Config.Name.ToUpperInvariant()));
            root.Children.Add(new Border { Height = 1, Background = _accent, Margin = new Thickness(0, 3, 0, 5) });
        }
        if (Config.Horizontal)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < _rows.Count; i++)
            {
                var col = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 16, 0, 0, 0), MinWidth = 40 };
                var l = Tb(12, MutedBrush); var v = Tb(13, TextBrush, bold: true);
                col.Children.Add(l); col.Children.Add(v);
                ColumnDefinition? f = null, rst = null; Border? fgBar = null;
                if (Config.Bars) { var (bar, ff, rr, fg) = NewBar(); col.Children.Add(bar); f = ff; rst = rr; fgBar = fg; }
                sp.Children.Add(col);
                _cells.Add(new Cell(l, v, f, rst, fgBar));
            }
            root.Children.Add(sp);
        }
        else
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int row = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = Tb(12, MutedBrush) ; l.MaxWidth = 170; l.Margin = new Thickness(0, 1, 0, 1);
                var v = Tb(13, TextBrush, bold: true); v.HorizontalAlignment = HorizontalAlignment.Right; v.Margin = new Thickness(0, 1, 0, 1);
                Grid.SetRow(l, row); Grid.SetColumn(l, 0); Grid.SetRow(v, row); Grid.SetColumn(v, 2);
                grid.Children.Add(l); grid.Children.Add(v);
                ColumnDefinition? f = null, rst = null; Border? fgBar = null;
                row++;
                if (Config.Bars)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var (bar, ff, rr, fg) = NewBar(); f = ff; rst = rr; fgBar = fg;
                    Grid.SetRow(bar, row); Grid.SetColumn(bar, 0); Grid.SetColumnSpan(bar, 3);
                    grid.Children.Add(bar);
                    row++;
                }
                _cells.Add(new Cell(l, v, f, rst, fgBar));
            }
            root.Children.Add(grid);
        }
        if (_rows.Count == 0)
            root.Children.Add(Tb(12, MutedBrush).With(t => t.Text = "Sem itens. Escolha na página Widgets."));
        _box.Child = root;
        Fill();
    }

    private void Fill()
    {
        for (int i = 0; i < _cells.Count && i < _rows.Count; i++)
        {
            var (label, text, level, ratio) = _rows[i];
            var c = _cells[i];
            c.Label.Text = label;
            c.Value.Text = text;
            var brush = level switch { WidgetLevel.Ok => OkBrush, WidgetLevel.Warn => WarnBrush, WidgetLevel.Bad => BadBrush, _ => TextBrush };
            c.Value.Foreground = brush;
            if (c.Fill != null && c.Rest != null)
            {
                double r = ratio ?? 0;
                c.Fill.Width = new GridLength(r, GridUnitType.Star);
                c.Rest.Width = new GridLength(Math.Max(0.0001, 1 - r), GridUnitType.Star);
                if (c.BarFill != null) c.BarFill.Background = brush;   // a barra tem a cor do valor
            }
        }
    }

    // ------------------------------------------------------------------ estilo da janela / posição (pixels)

    private void ApplyStyles()
    {
        if (_hwnd == IntPtr.Zero) return;
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED;
        ex = Config.ClickThrough ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex);
    }

    public (int X, int Y, int W, int H) RectPx()
    {
        if (_hwnd != IntPtr.Zero && Native.GetWindowRect(_hwnd, out var r)) return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return (_pendingX, _pendingY, (int)ActualWidth, (int)ActualHeight);
    }

    public void MovePx(int x, int y)
    {
        _pendingX = x; _pendingY = y;
        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    private void CommitPosition()
    {
        var (x, y, _, _) = RectPx();
        Config.X = x; Config.Y = y;
        Moved?.Invoke(this);
    }

    /// <summary>Se o widget ficou fora de qualquer monitor (monitor removido, resolução menor), volta para o canto da principal.</summary>
    public void EnsureOnScreen()
    {
        var (x, y, w, h) = RectPx();
        var screens = Native.Monitors(work: false);
        if (screens.Count == 0 || WidgetGeometry.IsOnAnyScreen(x, y, w, h, screens)) return;
        var work = Native.Monitors(work: true);
        var a = work[0];
        MovePx(a.Left + 40, a.Top + 40);
        CommitPosition();
    }

    public void PlaceInCorner(bool left, bool top)
    {
        var (x, y, w, h) = RectPx();
        var area = WidgetGeometry.AreaAt(x + w / 2, y + h / 2, Native.Monitors(work: true));
        if (area is not { } a) return;
        var (nx, ny) = WidgetGeometry.Corner(a, w, h, left, top);
        MovePx(nx, ny);
        CommitPosition();
    }

    // ------------------------------------------------------------------ mouse

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { OpenSettingsRequested?.Invoke(this); e.Handled = true; return; }
        if (Config.Locked || Config.ClickThrough || _hwnd == IntPtr.Zero) return;
        Native.GetCursorPos(out var p);
        var (x, y, _, _) = RectPx();
        _dragStartCursor = (p.X, p.Y); _dragStartWin = (x, y);
        _dragging = CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        Native.GetCursorPos(out var p);
        int nx = _dragStartWin.X + p.X - _dragStartCursor.X, ny = _dragStartWin.Y + p.Y - _dragStartCursor.Y;
        var (_, _, w, h) = RectPx();
        if (WidgetGeometry.AreaAt(p.X, p.Y, Native.Monitors(work: true)) is { } area)
            (nx, ny) = WidgetGeometry.Snap(nx, ny, w, h, area);
        MovePx(nx, ny);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        CommitPosition();
    }

    private void BuildMenu()
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Abrir configurações" };
        open.Click += (_, _) => OpenSettingsRequested?.Invoke(this);
        var lockItem = new MenuItem { Header = "Travar posição", IsCheckable = true, IsChecked = Config.Locked };
        lockItem.Click += (_, _) => { Config.Locked = lockItem.IsChecked; LockChanged?.Invoke(this); };
        var hide = new MenuItem { Header = "Ocultar este widget" };
        hide.Click += (_, _) => HideRequested?.Invoke(this);
        menu.Items.Add(open); menu.Items.Add(lockItem); menu.Items.Add(new Separator()); menu.Items.Add(hide);
        ContextMenu = menu;
    }

    // ------------------------------------------------------------------ Win32

    private static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
        public const uint SWP_NOSIZE = 1, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data);

        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO info);

        /// <summary>Monitores (área útil sem a barra de tarefas se work=true; senão a tela inteira). A principal vem primeiro.</summary>
        public static List<PxRect> Monitors(bool work)
        {
            var list = new List<(PxRect Rect, bool Primary)>();
            MonitorEnumProc proc = (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMon, ref mi))
                {
                    var r = work ? mi.rcWork : mi.rcMonitor;
                    list.Add((new PxRect(r.Left, r.Top, r.Right, r.Bottom), (mi.dwFlags & 1) != 0));
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);
            return list.OrderByDescending(x => x.Primary).Select(x => x.Rect).ToList();
        }
    }
}

internal static class ControlExt
{
    public static T With<T>(this T t, Action<T> a) { a(t); return t; }
}
