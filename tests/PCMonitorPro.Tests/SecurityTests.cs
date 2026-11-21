using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace PCMonitorPro.Tests;

public class TrustedPublisherTests
{
    [Theory]
    [InlineData("https://us.download.nvidia.com/Windows/x.exe", true)]
    [InlineData("https://nvidia.com/a.exe", true)]
    [InlineData("https://www.nvidia.com/Download", true)]
    [InlineData("http://us.download.nvidia.com/x.exe", false)]            // sem https
    [InlineData("https://evil-nvidia.com/x.exe", false)]                  // sufixo parecido
    [InlineData("https://nvidia.com.evil.io/x.exe", false)]               // domínio do atacante
    [InlineData("https://evil.io/?u=https://nvidia.com/x.exe", false)]
    [InlineData("ftp://nvidia.com/x.exe", false)]
    [InlineData("não é url", false)]
    public void IsAllowedHost_Nvidia(string url, bool expected) =>
        Assert.Equal(expected, TrustedPublisher.IsAllowedHost(url, TrustedPublisher.NvidiaHosts));

    [Fact]
    public void IsAllowedHost_AmdAndCorsair()
    {
        Assert.True(TrustedPublisher.IsAllowedHost("https://drivers.amd.com/drivers/x.exe", TrustedPublisher.AmdHosts));
        Assert.False(TrustedPublisher.IsAllowedHost("https://drivers.amd.com.evil.io/x.exe", TrustedPublisher.AmdHosts));
        Assert.True(TrustedPublisher.IsAllowedHost("https://github.com/CorsairOfficial/cue-sdk/releases/download/v4/a.zip", TrustedPublisher.CorsairHosts));
        Assert.True(TrustedPublisher.IsAllowedHost("https://objects.githubusercontent.com/x", TrustedPublisher.CorsairHosts));
        Assert.False(TrustedPublisher.IsAllowedHost("https://notgithub.com/x", TrustedPublisher.CorsairHosts));
    }

    [Theory]
    [InlineData("CN=\"Corsair Memory, Inc.\", O=\"Corsair Memory, Inc.\", L=Fremont, S=California, C=US", true)]
    [InlineData("CN=NVIDIA Corporation, O=NVIDIA Corporation, L=Santa Clara, S=California, C=US", false)]   // não é Corsair
    [InlineData("CN=Evil, O=\"Corsair Memory, Inc. Fake\", C=US", false)]                                   // "contém" não basta
    [InlineData("CN=Evil, O=Corsair Memory, Inc. Fake, C=US", false)]                                       // assunto malformado: recusa, sem exceção
    [InlineData("", false)]
    [InlineData("CN=\"Corsair Memory, Inc.\", O=Someone Else, C=US", false)]                                // só o CN não vale
    public void OrgMatches_Corsair(string subject, bool expected) =>
        Assert.Equal(expected, TrustedPublisher.OrgMatches(subject, TrustedPublisher.CorsairOrgs));

    [Fact]
    public void OrgMatches_Nvidia_IsExact()
    {
        Assert.True(TrustedPublisher.OrgMatches("CN=NVIDIA Corporation, O=NVIDIA Corporation, C=US", TrustedPublisher.NvidiaOrgs));
        Assert.False(TrustedPublisher.OrgMatches("CN=x, O=NVIDIA Corporation Ltd, C=US", TrustedPublisher.NvidiaOrgs));
    }

    [Fact]
    public void RequireSigner_RejectsUnsignedFile()
    {
        string f = Path.Combine(Path.GetTempPath(), "pmp-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(f, new byte[200_000]);
        try { Assert.Throws<InvalidOperationException>(() => TrustedPublisher.RequireSigner(f, TrustedPublisher.CorsairOrgs)); }
        finally { File.Delete(f); }
    }

    /// <summary>Teste manual: aponte PMP_SIGNED_CORSAIR_DLL para a iCUESDK.x64_2019.dll oficial (não vai no repositório).</summary>
    [Fact]
    public void RequireSigner_AcceptsRealCorsairDll_WhenProvided()
    {
        string? dll = Environment.GetEnvironmentVariable("PMP_SIGNED_CORSAIR_DLL");
        if (string.IsNullOrEmpty(dll) || !File.Exists(dll)) return;
        Assert.Equal("Corsair Memory, Inc.", TrustedPublisher.RequireSigner(dll, TrustedPublisher.CorsairOrgs));
        Assert.Equal("Valid Corsair", CorsairSdkInstaller.Verify(dll).Contains("Corsair") ? "Valid Corsair" : "?");
    }

    [Fact]
    public void RequireSigner_ValidSignatureFromOtherCompanyIsRejected()
    {
        // notepad.exe tem assinatura válida da Microsoft, mas a empresa não está na lista da Corsair
        string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Assert.Throws<InvalidOperationException>(() => TrustedPublisher.RequireSigner(notepad, TrustedPublisher.CorsairOrgs));
    }
}

public class SecureDirTests
{
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System_ = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier Everyone = new(WellKnownSidType.WorldSid, null);

    private static DirectorySecurity Make(SecurityIdentifier owner, params (SecurityIdentifier Sid, FileSystemRights Rights)[] rules)
    {
        var sec = new DirectorySecurity();
        sec.SetOwner(owner);
        foreach (var (sid, rights) in rules)
            sec.AddAccessRule(new FileSystemAccessRule(sid, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                                                       PropagationFlags.None, AccessControlType.Allow));
        return sec;
    }

    [Fact]
    public void AdminOnly_IsSecure() =>
        Assert.Null(SecureDir.Inspect(Make(Admins, (Admins, FileSystemRights.FullControl), (System_, FileSystemRights.FullControl))));

    [Fact]
    public void UsersCanRead_IsStillSecure() =>
        Assert.Null(SecureDir.Inspect(Make(Admins, (Admins, FileSystemRights.FullControl), (Users, FileSystemRights.ReadAndExecute))));

    [Theory]
    [InlineData(FileSystemRights.Write)]
    [InlineData(FileSystemRights.Modify)]
    [InlineData(FileSystemRights.CreateFiles)]
    [InlineData(FileSystemRights.AppendData)]
    [InlineData(FileSystemRights.Delete)]
    [InlineData(FileSystemRights.ChangePermissions)]
    [InlineData(FileSystemRights.FullControl)]
    public void UsersCanWrite_IsInsecure(FileSystemRights rights) =>
        Assert.NotNull(SecureDir.Inspect(Make(Admins, (Admins, FileSystemRights.FullControl), (Users, rights))));

    [Fact]
    public void EveryoneFullControl_IsInsecure() =>
        Assert.NotNull(SecureDir.Inspect(Make(Admins, (Everyone, FileSystemRights.FullControl))));

    [Fact]
    public void OwnerIsNormalUser_IsInsecure()
    {
        var me = WindowsIdentity.GetCurrent().User!;
        if (me.Equals(Admins) || me.Equals(System_)) return;   // sessão que já é dona como administrador: nada a testar
        Assert.NotNull(SecureDir.Inspect(Make(me, (Admins, FileSystemRights.FullControl))));
    }

    [Fact]
    public void Create_RejectsPathTraversal()
    {
        string root = Path.Combine(Path.GetTempPath(), "pmp-root-" + Guid.NewGuid().ToString("N"));
        SecureDir.RootOverride = root;
        try { Assert.ThrowsAny<Exception>(() => SecureDir.Create(@"..\fora")); }
        finally { SecureDir.RootOverride = null; try { Directory.Delete(root, true); } catch { } }
    }
}

public class ProcessLauncherTests
{
    [Fact]
    public void OpenUri_RejectsDangerousSchemes()
    {
        Assert.Throws<InvalidOperationException>(() => ProcessLauncher.OpenUriDeElevated("file:///C:/Windows/System32/calc.exe"));
        Assert.Throws<InvalidOperationException>(() => ProcessLauncher.OpenUriDeElevated("javascript:alert(1)"));
        Assert.Throws<InvalidOperationException>(() => ProcessLauncher.OpenUriDeElevated("não é uri"));
    }

    [Fact]
    public void StartDeElevated_RunsProgramAndPassesArguments()
    {
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        using var p = ProcessLauncher.StartDeElevated(cmd, "/c exit 7", Environment.SystemDirectory);
        Assert.NotNull(p);
        Assert.True(p!.WaitForExit(15000));
        Assert.Equal(7, p.ExitCode);
    }
}
