using Xunit;

namespace PCMonitorPro.Tests;

public class CliCommandsTests
{
    [Theory]
    [InlineData("--ativar-inicio", CliCommand.EnableAutostart)]
    [InlineData("--ATIVAR-INICIO", CliCommand.EnableAutostart)]
    [InlineData("--desativar-inicio", CliCommand.DisableAutostart)]
    public void Parse_recognizes_installer_commands(string arg, CliCommand expected)
        => Assert.Equal(expected, CliCommands.Parse(new[] { arg }));

    [Fact]
    public void Parse_ignores_everything_else()
    {
        Assert.Equal(CliCommand.None, CliCommands.Parse(Array.Empty<string>()));
        Assert.Equal(CliCommand.None, CliCommands.Parse(new[] { "--bandeja" }));            // início automático normal abre a janela
        Assert.Equal(CliCommand.None, CliCommands.Parse(new[] { "--ativar-inicio", "--bandeja" }));   // mais de um argumento: não é o instalador
        Assert.Equal(CliCommand.None, CliCommands.Parse(new[] { "--ativar-inicio=sim" }));
        Assert.Equal(CliCommand.None, CliCommands.Parse(new[] { "arquivo.pmpperfil" }));
        Assert.Equal(CliCommand.None, CliCommands.Parse(new[] { "", "  " }));
    }

    [Fact]
    public void Arguments_match_what_the_installer_script_uses()
    {
        // installer\PCMonitorPro.iss chama o programa com estes argumentos: mudar um sem o outro quebra o instalador
        var iss = File.ReadAllText(FindRepoFile("installer", "PCMonitorPro.iss"));
        Assert.Contains($"Parameters: \"{AutoStart.EnableArgument}\"", iss);
        Assert.Contains($"Parameters: \"{AutoStart.DisableArgument}\"", iss);
        Assert.Contains("PrivilegesRequired=admin", iss);                    // Program Files: o que libera o "Iniciar com o Windows"
        Assert.Contains("DefaultDirName={autopf}", iss);
    }

    [Fact]
    public void Installer_app_id_is_stable_and_valid()
    {
        var iss = File.ReadAllText(FindRepoFile("installer", "PCMonitorPro.iss"));
        var m = System.Text.RegularExpressions.Regex.Match(iss, @"AppId=\{\{([0-9A-F\-]{36})\}");
        Assert.True(m.Success, "AppId não encontrado");
        Assert.True(Guid.TryParse(m.Groups[1].Value, out _));
    }

    private static string FindRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }
}
