namespace PCMonitorPro;

/// <summary>Mantém uma janela por widget visível e as alimenta com as leituras dos sensores a cada segundo.</summary>
public sealed class WidgetManager : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Dictionary<string, WidgetWindow> _wins = new();
    private int _catalogRows = -1;
    private Dictionary<string, WidgetMetric> _byId = WidgetCatalog.Base.ToDictionary(m => m.Id);

    /// <summary>Posição mudou (arrasto, canto) ou o usuário travou/ocultou pelo menu do widget: o chamador deve gravar as configurações.</summary>
    public event Action? Changed;
    /// <summary>Duplo clique ou "Abrir configurações" no widget.</summary>
    public event Action<string>? OpenSettingsRequested;

    public IReadOnlyList<WidgetMetric> Catalog { get; private set; } = WidgetCatalog.Base;
    public int OpenCount => _wins.Count;

    public WidgetManager(AppSettings settings) => _settings = settings;

    /// <summary>Faz as janelas abertas corresponderem às configurações (cria, atualiza, fecha).</summary>
    public void Sync()
    {
        foreach (var w in _settings.Widgets) w.Sanitize();
        // ids repetidos (arquivo editado à mão) viram um id novo, para cada widget ter a sua janela
        var seen = new HashSet<string>();
        foreach (var w in _settings.Widgets) if (!seen.Add(w.Id)) { w.Id = Guid.NewGuid().ToString("N")[..8]; seen.Add(w.Id); }

        foreach (var id in _wins.Keys.Where(id => _settings.Widgets.All(w => w.Id != id || !w.Visible)).ToList())
        {
            _wins[id].Close();
            _wins.Remove(id);
        }
        foreach (var cfg in _settings.Widgets.Where(w => w.Visible))
        {
            if (_wins.TryGetValue(cfg.Id, out var win)) { win.Configure(cfg); continue; }
            win = new WidgetWindow(cfg);
            win.Moved += _ => Changed?.Invoke();
            win.LockChanged += _ => Changed?.Invoke();
            win.OpenSettingsRequested += w => OpenSettingsRequested?.Invoke(w.Config.Id);
            win.HideRequested += w => { w.Config.Visible = false; Sync(); Changed?.Invoke(); };
            _wins[cfg.Id] = win;
            win.Show();
        }
    }

    /// <summary>Reconstrói a lista de métricas (base + sensores) quando o número de sensores muda.</summary>
    public void RefreshCatalog(SensorMonitor m)
    {
        if (m.Rows.Count == _catalogRows) return;
        _catalogRows = m.Rows.Count;
        var list = WidgetCatalog.Build(m);
        Catalog = list;
        _byId = new Dictionary<string, WidgetMetric>();
        foreach (var x in list) _byId[x.Id] = x;
    }

    public WidgetMetric? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Chame a cada segundo. Não lê nada se não houver widget na tela.</summary>
    public void Tick(SensorMonitor m)
    {
        if (_wins.Count == 0 || !m.IsOpen) return;
        try
        {
            RefreshCatalog(m);
            var values = WidgetCatalog.ReadValues(m);
            foreach (var (id, win) in _wins)
                win.SetRows(WidgetFormat.BuildRows(win.Config, _byId, values));
        }
        catch (Exception ex) { AppLog.Debug(ex); }
    }

    public void PlaceInCorner(string id, bool left, bool top)
    {
        if (_wins.TryGetValue(id, out var w)) w.PlaceInCorner(left, top);
    }

    public void Dispose()
    {
        foreach (var w in _wins.Values) { try { w.Close(); } catch (Exception ex) { AppLog.Debug(ex); } }
        _wins.Clear();
    }
}
