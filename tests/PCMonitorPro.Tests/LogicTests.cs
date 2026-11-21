using Xunit;

namespace PCMonitorPro.Tests;

public class VdfTests
{
    private const string Sample = "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n" +
                                  "\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"730\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-novid\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n";

    [Fact]
    public void Parse_ReadsNestedValues()
    {
        var root = Vdf.Parse(Sample);
        var app = root.Child("UserLocalConfigStore")!.Child("Software")!.Child("Valve")!.Child("Steam")!.Child("apps")!.Child("730")!;
        Assert.Equal("-novid", app.Get("LaunchOptions"));
    }

    [Fact]
    public void Serialize_RoundTrips_AndKeepsEdits()
    {
        var root = Vdf.Parse(Sample);
        var app = root.Child("UserLocalConfigStore")!.Child("Software")!.Child("Valve")!.Child("Steam")!.Child("apps")!.Child("730")!;
        app.Set("LaunchOptions", "-novid -fullscreen");
        var again = Vdf.Parse(Vdf.Serialize(root));
        var app2 = again.Child("UserLocalConfigStore")!.Child("Software")!.Child("Valve")!.Child("Steam")!.Child("apps")!.Child("730")!;
        Assert.Equal("-novid -fullscreen", app2.Get("LaunchOptions"));
    }

    [Fact]
    public void Values_WithQuotesAndBackslashes_Survive()
    {
        var root = Vdf.Parse("\"a\"\n{\n\t\"p\"\t\t\"C:\\\\Jogos\\\\x \\\"y\\\"\"\n}\n");
        string v = root.Child("a")!.Get("p")!;
        var again = Vdf.Parse(Vdf.Serialize(root));
        Assert.Equal(v, again.Child("a")!.Get("p"));
    }
}

public class StartupCommandTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --min", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Tools\\a.exe -x", "C:\\Tools\\a.exe")]
    [InlineData("\"\"", null)]
    [InlineData("", null)]
    public void ExeFromCommand(string cmd, string? expected) =>
        Assert.Equal(expected, StartupManager.ExeFromCommand(cmd));

    [Fact]
    public void ExeFromCommand_FindsSystemTools()
    {
        string? exe = StartupManager.ExeFromCommand("cmd.exe /c echo oi");
        Assert.NotNull(exe);
        Assert.EndsWith("cmd.exe", exe, StringComparison.OrdinalIgnoreCase);
    }
}

public class GameArgsTests
{
    [Fact]
    public void Source_Borderless_1080p()
    {
        var p = new GameProfile { Style = "source", WinMode = "borderless", Res = "1920x1080", Hz = 144, Extra = "-novid" };
        Assert.Equal("-windowed -noborder -w 1920 -h 1080 -freq 144 -novid", GameLauncher.BuildArgs(p));
    }

    [Fact]
    public void Unity_Fullscreen()
    {
        var p = new GameProfile { Style = "unity", WinMode = "full", Res = "2560x1440" };
        Assert.Equal("-screen-fullscreen 1 -screen-width 2560 -screen-height 1440", GameLauncher.BuildArgs(p));
    }

    [Fact]
    public void Unreal_Window()
    {
        var p = new GameProfile { Style = "unreal", WinMode = "window", Res = "1280x720" };
        Assert.Equal("-windowed -ResX=1280 -ResY=720", GameLauncher.BuildArgs(p));
    }

    [Fact]
    public void Defaults_GiveNoArguments() => Assert.Equal("", GameLauncher.BuildArgs(new GameProfile()));

    [Fact]
    public void InvalidResolution_IsIgnored() =>
        Assert.Equal("", GameLauncher.BuildArgs(new GameProfile { Res = "grande" }));
}
