using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace PCMonitorPro;

/// <summary>Estado da tarefa "iniciar com o Windows".</summary>
public sealed record AutoStartState(bool Exists, string? Command, bool IsThisExe);

/// <summary>
/// Iniciar com o Windows por uma tarefa do Agendador (ao entrar no Windows, com privilégios elevados). O programa precisa de
/// administrador para ler os sensores, e uma entrada comum de inicialização não abre programa elevado sem UAC.
/// Como a tarefa roda o .exe como administrador, só é criada se ninguém que não seja administrador puder trocar o .exe
/// (nem a pasta dele); do contrário um programa comum viraria administrador na próxima entrada no Windows.
/// </summary>
public static class AutoStart
{
    public const string TaskName = "PCMonitorPro";
    /// <summary>Argumento do início automático: abre direto na bandeja (se a opção de bandeja estiver ligada).</summary>
    public const string TrayArgument = "--bandeja";
    /// <summary>Usados pelo instalador (veja CliCommands).</summary>
    public const string EnableArgument = "--ativar-inicio";
    public const string DisableArgument = "--desativar-inicio";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string? ExePath => Environment.ProcessPath;

    /// <summary>Motivo pelo qual a pasta/arquivo não é segura para rodar elevado; null se estiver protegida.</summary>
    public static string? CheckFolder(string exePath)
    {
        try
        {
            var file = new FileInfo(exePath);
            if (!file.Exists) return "o arquivo do programa não foi encontrado";
            var fileReason = SecureDir.Inspect(file.GetAccessControl());
            if (fileReason != null) return $"o programa ({fileReason})";
            var dir = file.Directory;
            bool first = true;
            while (dir != null)
            {
                var r = SecureDir.Inspect(dir.GetAccessControl(), ancestor: !first);
                if (r != null) return $"a pasta {dir.FullName} ({r})";
                first = false;
                dir = dir.Parent;
            }
            return null;
        }
        catch (Exception ex) { AppLog.Warn("Autostart: não foi possível conferir as permissões", ex); return "não foi possível conferir as permissões"; }
    }

    /// <summary>XML da tarefa. Sem limite de tempo (o padrão do Windows encerraria o programa após 3 dias) e sem parar na bateria.</summary>
    public static string BuildTaskXml(string exePath, string userId)
    {
        var doc = new XDocument(new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task", new XAttribute("version", "1.4"),
                new XElement(Ns + "RegistrationInfo", new XElement(Ns + "Description", "Inicia o PC Monitor Pro ao entrar no Windows.")),
                new XElement(Ns + "Triggers",
                    new XElement(Ns + "LogonTrigger",
                        new XElement(Ns + "Enabled", "true"),
                        new XElement(Ns + "UserId", userId),
                        new XElement(Ns + "Delay", "PT10S"))),
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", userId),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "HighestAvailable"))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "AllowHardTerminate", "true"),
                    new XElement(Ns + "StartWhenAvailable", "false"),
                    new XElement(Ns + "AllowStartOnDemand", "true"),
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "ExecutionTimeLimit", "PT0S")),
                new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", exePath),
                        new XElement(Ns + "Arguments", TrayArgument),
                        new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(exePath))))));
        return doc.Declaration + Environment.NewLine + doc.Root;
    }

    /// <summary>Command da tarefa a partir do XML devolvido por "schtasks /query /xml" (null se não houver).</summary>
    public static string? ParseCommand(string xml)
    {
        try
        {
            int i = xml.IndexOf('<');
            return XDocument.Parse(i > 0 ? xml[i..] : xml).Descendants(Ns + "Exec").Elements(Ns + "Command").FirstOrDefault()?.Value.Trim();
        }
        catch (Exception ex) { AppLog.Debug(ex); return null; }
    }

    public static AutoStartState GetState()
    {
        var (code, output) = Schtasks($"/query /tn {TaskName} /xml");
        if (code != 0) return new AutoStartState(false, null, false);
        var cmd = ParseCommand(output);
        bool same = cmd != null && ExePath != null && string.Equals(Path.GetFullPath(cmd.Trim('"')), Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase);
        return new AutoStartState(true, cmd, same);
    }

    /// <summary>Cria ou atualiza a tarefa. Devolve (ok, mensagem para a tela).</summary>
    public static (bool Ok, string Message) Enable()
    {
        if (ExePath is not { } exe) return (false, "Não foi possível descobrir onde o programa está.");
        if (!ProcessLauncher.IsElevated()) return (false, "Abra o programa como administrador para ativar.");
        var reason = CheckFolder(exe);
        if (reason != null)
            return (false, $"Não ativado por segurança: {reason} pode ser alterado por quem não é administrador. " +
                           "Como a tarefa roda o programa com privilégios de administrador, instale-o em uma pasta protegida " +
                           "(por exemplo, dentro de C:\\Program Files) e ative de lá.");
        try
        {
            string dir = SecureDir.Create("autostart");   // só administradores gravam aqui: o XML não pode ser trocado antes de ler
            string xmlPath = Path.Combine(dir, "tarefa.xml");
            File.WriteAllText(xmlPath, BuildTaskXml(exe, WindowsIdentity.GetCurrent().Name), Encoding.Unicode);
            var (code, output) = Schtasks($"/create /tn {TaskName} /xml \"{xmlPath}\" /f");
            try { File.Delete(xmlPath); } catch (Exception ex) { AppLog.Debug(ex); }
            return code == 0 ? (true, "Ativado: o programa abre sozinho, como administrador, quando você entrar no Windows.")
                             : (false, "Falha ao criar a tarefa: " + output.Trim());
        }
        catch (Exception ex) { AppLog.Warn("Autostart: falha ao ativar", ex); return (false, "Falha ao ativar: " + ex.Message); }
    }

    public static (bool Ok, string Message) Disable()
    {
        if (!GetState().Exists) return (true, "Desativado.");
        var (code, output) = Schtasks($"/delete /tn {TaskName} /f");
        return code == 0 ? (true, "Desativado.") : (false, "Falha ao remover a tarefa: " + output.Trim());
    }

    private static (int Code, string Output) Schtasks(string args)
    {
        try
        {
            string exe = Path.Combine(Environment.SystemDirectory, "schtasks.exe");   // caminho absoluto, não procura no PATH
            Encoding enc;
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                enc = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            }
            catch { enc = Encoding.UTF8; }
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = enc, StandardErrorEncoding = enc,
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("schtasks não iniciou");
            var err = p.StandardError.ReadToEndAsync();
            string o = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(20000)) { try { p.Kill(true); } catch { } return (-1, "tempo esgotado"); }
            return (p.ExitCode, o + err.GetAwaiter().GetResult());
        }
        catch (Exception ex) { AppLog.Debug(ex); return (-1, ex.Message); }
    }
}
