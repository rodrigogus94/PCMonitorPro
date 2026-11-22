namespace PCMonitorPro;

public enum CliCommand { None, EnableAutostart, DisableAutostart }

/// <summary>
/// Comandos de linha usados pelo instalador, sem abrir janela: <c>--ativar-inicio</c> e <c>--desativar-inicio</c>
/// (cria/remove a tarefa "iniciar com o Windows"). O resultado vai para o log e para o código de saída (0 = ok).
/// </summary>
public static class CliCommands
{
    public static CliCommand Parse(IEnumerable<string> args)
    {
        var list = args.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (list.Count != 1) return CliCommand.None;   // exatamente um argumento: evita confundir com --bandeja e outros
        return list[0].ToLowerInvariant() switch
        {
            AutoStart.EnableArgument => CliCommand.EnableAutostart,
            AutoStart.DisableArgument => CliCommand.DisableAutostart,
            _ => CliCommand.None,
        };
    }

    /// <summary>Executa o comando e devolve o código de saída.</summary>
    public static int Run(CliCommand cmd)
    {
        var (ok, message) = cmd switch
        {
            CliCommand.EnableAutostart => AutoStart.Enable(),
            CliCommand.DisableAutostart => AutoStart.Disable(),
            _ => (true, ""),
        };
        if (ok) AppLog.Info($"Linha de comando {cmd}: {message}"); else AppLog.Warn($"Linha de comando {cmd} falhou: {message}");
        return ok ? 0 : 1;
    }
}
