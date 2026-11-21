using System.IO;
using System.Text;

namespace PCMonitorPro;

/// <summary>Relatórios de diagnóstico salvos em Documentos\PCMonitor.</summary>
public static class ReportStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCMonitor");

    /// <summary>Grava o relatório em um arquivo novo (nunca sobrescreve) e devolve o caminho.</summary>
    public static string Save(string text, DateTime? now = null, string? folder = null)
    {
        var dir = folder ?? Folder;
        Directory.CreateDirectory(dir);
        var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(dir, $"diagnostico-{stamp}.txt");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"diagnostico-{stamp}-{i}.txt");
        File.WriteAllText(path, text, new UTF8Encoding(true));
        return path;
    }
}
