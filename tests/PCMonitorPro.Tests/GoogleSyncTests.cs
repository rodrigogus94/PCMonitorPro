using System.Net;
using System.Net.Http;
using Xunit;

namespace PCMonitorPro.Tests;

[Collection("pasta-de-dados")]
public class GoogleSyncTests : IDisposable
{
    private readonly string _old = AppSettings.Folder;
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "pmp-gs-" + Guid.NewGuid().ToString("N"));
    private readonly FakeGoogle _g = new();

    public GoogleSyncTests() { Directory.CreateDirectory(_tmp); AppSettings.Folder = _tmp; AppLog.UsePath(Path.Combine(_tmp, "t.log")); }
    public void Dispose() { _g.Dispose(); AppSettings.Folder = _old; AppLog.UsePath(null); try { Directory.Delete(_tmp, true); } catch { } }

    private AppSettings NewSettings() => new() { GoogleClientId = _g.ClientId, GoogleClientSecret = _g.ClientSecret };

    /// <summary>Faz o papel do navegador: abre o endereço de login, segue o redirecionamento do Google e entrega o código ao programa.</summary>
    private Func<string, Task> Browser(Func<string, string>? tamper = null) => async url =>
    {
        using var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var r = await h.GetAsync(url);
        string back = r.Headers.Location!.ToString();
        await h.GetAsync(tamper?.Invoke(back) ?? back);
    };

    private GoogleSyncService Service(AppSettings s, Func<string, Task>? browser = null) =>
        new(s, new HttpClient(), _g.Endpoints, browser ?? Browser());

    private async Task<GoogleSyncService> Connected(AppSettings s)
    {
        var svc = Service(s);
        await svc.LoginAsync(CancellationToken.None);
        return svc;
    }

    // ---------- PKCE ----------

    [Fact]
    public void Pkce_matches_rfc7636_example()
        => Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Pkce_verifier_is_random_and_in_allowed_range()
    {
        var a = Pkce.Create(); var b = Pkce.Create();
        Assert.NotEqual(a.Verifier, b.Verifier);
        Assert.InRange(a.Verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", a.Verifier);
        Assert.Equal(Pkce.Challenge(a.Verifier), a.Challenge);
    }

    // ---------- login ----------

    [Fact]
    public async Task Login_stores_encrypted_tokens_and_email()
    {
        var s = NewSettings();
        var email = await Service(s).LoginAsync(CancellationToken.None);
        Assert.Equal(_g.Email, email);
        Assert.Equal(_g.RefreshToken, s.GoogleRefreshToken);
        Assert.Contains("drive.appdata", _g.LastScope);
        Assert.Contains("openid", _g.LastScope);

        // nada em texto puro no settings.json
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        Assert.DoesNotContain(_g.RefreshToken, json);
        Assert.DoesNotContain(_g.ClientSecret, json);
        Assert.DoesNotContain("AT-", json);
        Assert.NotNull(s.GoogleRefreshTokenProtected);
    }

    [Fact]
    public async Task Login_without_client_id_explains_what_to_do()
    {
        var ex = await Assert.ThrowsAsync<GoogleSyncException>(() => Service(new AppSettings()).LoginAsync(CancellationToken.None));
        Assert.Contains("GOOGLE.md", ex.Message);
    }

    [Fact]
    public async Task Login_denied_by_user_stores_nothing()
    {
        var s = NewSettings();
        var svc = Service(s, async url =>
        {
            var redirect = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["redirect_uri"]!;
            using var h = new HttpClient();
            await h.GetAsync(redirect + "?error=access_denied");
        });
        var ex = await Assert.ThrowsAsync<GoogleSyncException>(() => svc.LoginAsync(CancellationToken.None));
        Assert.Contains("não autorizou", ex.Message);
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public async Task Login_rejects_wrong_state()
    {
        var s = NewSettings();
        var svc = Service(s, Browser(back => back[..back.IndexOf("&state=", StringComparison.Ordinal)] + "&state=outro"));
        var ex = await Assert.ThrowsAsync<GoogleSyncException>(() => svc.LoginAsync(CancellationToken.None));
        Assert.Contains("estado", ex.Message);
        Assert.False(svc.IsConnected);
        Assert.Equal(0, _g.TokenCalls);   // nem chegou a trocar o código
    }

    [Fact]
    public async Task Login_times_out_when_user_never_finishes()
    {
        var svc = Service(NewSettings(), _ => Task.CompletedTask);
        var ex = await Assert.ThrowsAsync<GoogleSyncException>(() => svc.LoginAsync(CancellationToken.None, TimeSpan.FromMilliseconds(300)));
        Assert.Contains("a tempo", ex.Message);
    }

    [Fact]
    public async Task Login_reports_when_browser_cannot_be_opened()
    {
        var svc = Service(NewSettings(), _ => throw new InvalidOperationException("sem navegador"));
        var ex = await Assert.ThrowsAsync<GoogleSyncException>(() => svc.LoginAsync(CancellationToken.None));
        Assert.Contains("abrir o navegador", ex.Message);
    }

    [Fact]
    public async Task Login_can_be_cancelled()
    {
        using var cts = new CancellationTokenSource(200);
        var svc = Service(NewSettings(), _ => Task.CompletedTask);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.LoginAsync(cts.Token, TimeSpan.FromSeconds(30)));
    }

    // ---------- enviar, listar, baixar ----------

    [Fact]
    public async Task Upload_creates_then_updates_the_same_file_for_this_computer()
    {
        var s = NewSettings();
        var svc = await Connected(s);
        await svc.UploadAsync("{\"v\":1}", "PC-SALA");
        await svc.UploadAsync("{\"v\":2}", "PC-SALA");

        var file = Assert.Single(_g.Files.Values);
        Assert.Equal(GoogleSyncService.FileNameFor(s.MachineId!), file.Name);
        Assert.DoesNotContain(Environment.MachineName, file.Name);   // o nome do arquivo usa um id aleatório, não o nome da máquina
        Assert.Equal("{\"v\":2}", file.Content);
        var list = await svc.ListAsync();
        var p = Assert.Single(list);
        Assert.Equal(("PC-SALA", s.MachineId), (p.Computer, p.MachineId));
    }

    [Fact]
    public async Task Full_roundtrip_profile_goes_to_the_cloud_and_back_to_another_pc()
    {
        var a = NewSettings();
        a.AlertTempC = 82;
        a.Widgets.Add(new WidgetConfig { Name = "Sala" });
        a.BenchRuns.Add(new BenchRun(new DateTime(2026, 6, 1), 9000, 1500, false, "Ryzen"));
        var svcA = await Connected(a);
        await svcA.UploadAsync(ProfileTransfer.Export(a, Enum.GetValues<ProfileSection>(), true), "PC-A");

        var b = NewSettings();                      // outro computador, mesma conta
        var svcB = await Connected(b);
        var cloud = await svcB.ListAsync();
        var mine = Assert.Single(cloud);
        Assert.Equal("PC-A", mine.Computer);
        var file = ProfileTransfer.Read(await svcB.DownloadAsync(mine));
        ProfileTransfer.Apply(b, file, ProfileTransfer.Available(file));

        Assert.Equal(82, b.AlertTempC);
        Assert.Equal("Sala", Assert.Single(b.Widgets).Name);
        Assert.Single(b.BenchRuns);
        Assert.NotEqual(a.MachineId, b.EnsureMachineId());
    }

    [Fact]
    public async Task List_shows_all_computers_newest_first_and_handles_paging()
    {
        _g.PageSize = 1;
        var svcA = await Connected(NewSettings());
        await svcA.UploadAsync("{}", "PC-A");
        await Task.Delay(50);
        var svcB = await Connected(NewSettings());
        await svcB.UploadAsync("{}", "PC-B");
        await Task.Delay(50);
        var svcC = await Connected(NewSettings());
        await svcC.UploadAsync("{}", "PC-C");

        var list = await svcC.ListAsync();
        Assert.Equal(new[] { "PC-C", "PC-B", "PC-A" }, list.Select(p => p.Computer));
    }

    [Fact]
    public async Task Delete_removes_only_this_computers_profile()
    {
        var svcA = await Connected(NewSettings());
        var svcB = await Connected(NewSettings());
        await svcA.UploadAsync("{}", "PC-A");
        await svcB.UploadAsync("{}", "PC-B");
        Assert.True(await svcA.DeleteMineAsync());
        Assert.Equal("PC-B", Assert.Single(await svcB.ListAsync()).Computer);
        Assert.False(await svcA.DeleteMineAsync());   // já não havia
    }

    [Fact]
    public async Task Oversized_profile_is_refused_both_ways()
    {
        var svc = await Connected(NewSettings());
        await Assert.ThrowsAsync<GoogleSyncException>(() => svc.UploadAsync(new string('x', (int)ProfileTransfer.MaxBytes + 1), "PC"));
        await svc.UploadAsync("{}", "PC");
        _g.Files.Values.Single().Content = new string('x', (int)ProfileTransfer.MaxBytes + 1);   // alguém colocou um arquivo enorme na pasta
        var p = (await svc.ListAsync()).Single();
        await Assert.ThrowsAsync<GoogleSyncException>(() => svc.DownloadAsync(p));
    }

    // ---------- tokens ----------

    [Fact]
    public async Task Expired_access_token_is_renewed_transparently()
    {
        var svc = await Connected(NewSettings());
        await svc.UploadAsync("{}", "PC");
        int before = _g.RefreshCalls;
        _g.ExpireAccessToken();
        var list = await svc.ListAsync();       // leva 401, renova e repete
        Assert.Single(list);
        Assert.Equal(before + 1, _g.RefreshCalls);
    }

    [Fact]
    public async Task Revoked_permission_asks_to_login_again_and_clears_tokens()
    {
        var s = NewSettings();
        var svc = await Connected(s);
        _g.ExpireAccessToken();
        _g.InvalidGrant = true;
        var ex = await Assert.ThrowsAsync<GoogleSyncException>(() => svc.ListAsync());
        Assert.True(ex.NeedsLogin);
        Assert.False(svc.IsConnected);
        Assert.Null(s.GoogleRefreshTokenProtected);
    }

    [Fact]
    public async Task Not_connected_asks_to_login()
    {
        var ex = await Assert.ThrowsAsync<GoogleSyncException>(() => Service(NewSettings()).ListAsync());
        Assert.True(ex.NeedsLogin);
    }

    [Fact]
    public async Task Disconnect_revokes_and_forgets()
    {
        var s = NewSettings();
        var svc = await Connected(s);
        await svc.DisconnectAsync();
        Assert.True(_g.Revoked);
        Assert.False(svc.IsConnected);
        Assert.Null(s.GoogleEmail);
        Assert.Null(s.GoogleRefreshTokenProtected);
    }

    [Fact]
    public async Task Disconnect_works_even_without_network()
    {
        var s = NewSettings();
        var svc = await Connected(s);
        _g.Dispose();                           // Google "fora do ar"
        await svc.DisconnectAsync();
        Assert.False(svc.IsConnected);
    }

    // ---------- erros ----------

    [Fact]
    public async Task Drive_api_disabled_gives_an_actionable_message()
    {
        var svc = await Connected(NewSettings());
        _g.ForbidDrive = true;
        var ex = await Assert.ThrowsAsync<GoogleSyncException>(() => svc.ListAsync());
        Assert.Contains("negou", ex.Message);
        Assert.Contains("Drive API", ex.Message);
        Assert.False(ex.NeedsLogin);
    }

    [Fact]
    public async Task Network_failure_is_reported_in_portuguese()
    {
        var svc = await Connected(NewSettings());
        _g.Dispose();
        var ex = await Assert.ThrowsAsync<GoogleSyncException>(() => svc.ListAsync());
        Assert.Contains("Sem conexão", ex.Message);
    }

    // ---------- segurança dos dados ----------

    [Fact]
    public void Exported_profile_never_contains_google_data()
    {
        var s = NewSettings();
        s.GoogleRefreshToken = "RT-SECRETO"; s.GoogleEmail = "alguem@exemplo.com"; s.EnsureMachineId();
        var json = ProfileTransfer.Export(s, Enum.GetValues<ProfileSection>(), true);
        foreach (var bad in new[] { "RT-SECRETO", "alguem@exemplo.com", "Google", _g.ClientSecret, _g.ClientId, s.MachineId! })
            Assert.DoesNotContain(bad, json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("a.@@@.c")]
    [InlineData("a.e30.c")]   // {} sem e-mail
    public void EmailFromIdToken_is_tolerant(string? token) => Assert.Null(GoogleSyncService.EmailFromIdToken(token));

    [Fact]
    public void Machine_id_is_stable_and_random()
    {
        var a = new AppSettings(); var b = new AppSettings();
        Assert.Equal(a.EnsureMachineId(), a.EnsureMachineId());
        Assert.NotEqual(a.EnsureMachineId(), b.EnsureMachineId());
        Assert.Equal(32, a.MachineId!.Length);
    }
}
