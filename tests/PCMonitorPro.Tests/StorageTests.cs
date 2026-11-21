using Xunit;

namespace PCMonitorPro.Tests;

/// <summary>AppSettings e AppLog usam a pasta de dados estática: os testes rodam em série e apontam para uma pasta temporária.</summary>
[CollectionDefinition("pasta-de-dados", DisableParallelization = true)]
public class DataFolderCollection { }

[Collection("pasta-de-dados")]
public class AppSettingsTests : IDisposable
{
    private readonly string _old = AppSettings.Folder;
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "pmp-settings-" + Guid.NewGuid().ToString("N"));

    public AppSettingsTests()
    {
        Directory.CreateDirectory(_tmp);
        AppSettings.Folder = _tmp;
        AppLog.UsePath(Path.Combine(_tmp, "t.log"));
    }

    public void Dispose()
    {
        AppSettings.Folder = _old;
        AppLog.UsePath(null);
        try { Directory.Delete(_tmp, true); } catch { }
    }

    private string File_ => Path.Combine(_tmp, "settings.json");

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var s = AppSettings.Load();
        Assert.False(s.LightTheme);
        Assert.Empty(s.TweakBackup);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_AndKeepsPreviousAsBak()
    {
        var s = new AppSettings { LightTheme = true };
        s.TweakBackup["power"] = "381b4222";
        s.Save();
        s.LightTheme = false;
        s.Save();
        Assert.True(File.Exists(File_ + ".bak"));
        Assert.False(File.Exists(File_ + ".tmp"));
        var back = AppSettings.Load();
        Assert.False(back.LightTheme);
        Assert.Equal("381b4222", back.TweakBackup["power"]);
    }

    [Fact]
    public void CorruptFile_IsBackedUp_AndLastGoodBakIsUsed()
    {
        var s = new AppSettings();
        s.TweakBackup["gamedvr.a"] = "1";
        s.Save();
        s.TweakBackup["x"] = "y";
        s.Save();                                   // agora existe .bak com a versão anterior
        File.WriteAllText(File_, "{ isto não é json");
        var loaded = AppSettings.Load();
        Assert.Contains(Directory.GetFiles(_tmp), f => f.Contains(".corrompido-"));
        Assert.Equal("1", loaded.TweakBackup["gamedvr.a"]);   // restaurou do .bak: os valores originais não se perderam
    }

    [Fact]
    public void CorruptFile_WithoutBak_GivesDefaults_ButKeepsCopy()
    {
        File.WriteAllText(File_, "[1,2,3]");
        var loaded = AppSettings.Load();
        Assert.Empty(loaded.TweakBackup);
        Assert.Contains(Directory.GetFiles(_tmp), f => f.Contains(".corrompido-"));
    }

    [Fact]
    public void LockedFile_IsNotOverwritten()
    {
        var s = new AppSettings();
        s.TweakBackup["power"] = "abc";
        s.Save();
        string original = File.ReadAllText(File_);
        using (new FileStream(File_, FileMode.Open, FileAccess.Read, FileShare.None))   // outro programa segurando o arquivo
        {
            var loaded = AppSettings.Load();
            Assert.True(loaded.LoadFailedKeepFile);
            loaded.LightTheme = true;
            loaded.Save();                                                                // não pode destruir o original
        }
        Assert.Equal(original, File.ReadAllText(File_));
    }

    [Fact]
    public void ApiKey_IsNeverWrittenInPlainText()
    {
        var s = new AppSettings { HowManyFpsKey = "chave-secreta-123" };
        s.Save();
        string json = File.ReadAllText(File_);
        Assert.DoesNotContain("chave-secreta-123", json);
        Assert.Equal("chave-secreta-123", AppSettings.Load().HowManyFpsKey);
    }
}

[Collection("pasta-de-dados")]
public class AppLogTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "pmp-log-" + Guid.NewGuid().ToString("N"));

    public AppLogTests() { Directory.CreateDirectory(_tmp); AppLog.UsePath(Path.Combine(_tmp, "t.log")); }
    public void Dispose() { AppLog.UsePath(null); try { Directory.Delete(_tmp, true); } catch { } }

    [Fact]
    public void WritesLevelsAndException()
    {
        AppLog.Info("olá");
        AppLog.Error("falhou", new InvalidOperationException("motivo"));
        string text = File.ReadAllText(AppLog.FilePath);
        Assert.Contains("INFO olá", text);
        Assert.Contains("ERRO falhou :: InvalidOperationException: motivo", text);
    }

    [Fact]
    public void RepeatedMessages_AreThrottled()
    {
        for (int i = 0; i < 50; i++) AppLog.Warn("sensor X falhou", new IOException("lido de novo"));
        string text = File.ReadAllText(AppLog.FilePath);
        Assert.Equal(1, text.Split("sensor X falhou").Length - 1);
    }

    [Fact]
    public void Debug_RecordsWhereItHappened()
    {
        AppLog.Debug(new FormatException("x"));
        Assert.Contains("StorageTests.Debug_RecordsWhereItHappened", File.ReadAllText(AppLog.FilePath));
    }

    [Fact]
    public void NeverThrows_EvenWithInvalidPath()
    {
        AppLog.UsePath(Path.Combine(_tmp, "con:", "x?.log"));
        AppLog.Info("não pode lançar exceção");
    }
}
