using System.Xml.Linq;
using Xunit;

namespace PCMonitorPro.Tests;

public class AutoStartTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void TaskXml_runs_elevated_without_time_limit_or_battery_stop()
    {
        var x = XDocument.Parse(AutoStart.BuildTaskXml(@"C:\Program Files\PCMonitorPro\PCMonitorPro.exe", @"PC\Rodri"));
        Assert.Equal("HighestAvailable", x.Descendants(Ns + "RunLevel").Single().Value);
        Assert.Equal("InteractiveToken", x.Descendants(Ns + "LogonType").Single().Value);
        Assert.Equal("PT0S", x.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("false", x.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal("false", x.Descendants(Ns + "StopIfGoingOnBatteries").Single().Value);
        Assert.Equal(@"PC\Rodri", x.Descendants(Ns + "LogonTrigger").Elements(Ns + "UserId").Single().Value);
        Assert.Equal(@"C:\Program Files\PCMonitorPro", x.Descendants(Ns + "WorkingDirectory").Single().Value);
    }

    [Fact]
    public void TaskXml_escapes_special_characters_in_path()
    {
        const string exe = @"C:\Tools & More\<app>\PCMonitorPro.exe";
        var xml = AutoStart.BuildTaskXml(exe, @"PC\Rodri");
        Assert.DoesNotContain("<app>", xml);
        Assert.Equal(exe, AutoStart.ParseCommand(xml));
    }

    [Fact]
    public void ParseCommand_ignores_text_before_xml_and_bad_input()
    {
        var xml = AutoStart.BuildTaskXml(@"C:\a\b.exe", "u");
        Assert.Equal(@"C:\a\b.exe", AutoStart.ParseCommand("\n  " + xml));
        Assert.Null(AutoStart.ParseCommand("não é xml"));
    }

    [Fact]
    public void CheckFolder_accepts_system_folder_and_refuses_user_writable_one()
    {
        Assert.Null(AutoStart.CheckFolder(Path.Combine(Environment.SystemDirectory, "cmd.exe")));

        var dir = Path.Combine(Path.GetTempPath(), "pmp-as-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, "x.exe");
            File.WriteAllText(exe, "x");
            Assert.NotNull(AutoStart.CheckFolder(exe));   // pasta do usuário: um programa comum poderia trocar o .exe
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CheckFolder_refuses_missing_file()
        => Assert.NotNull(AutoStart.CheckFolder(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N") + ".exe")));

    [Fact]
    public void GetState_does_not_throw_without_task()
    {
        var st = AutoStart.GetState();
        if (!st.Exists) Assert.Null(st.Command);
    }
}
