using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCMonitorPro;

/// <summary>Endereços do Google. Nos testes apontam para um servidor local de mentira.</summary>
public sealed record GoogleEndpoints(string Auth, string Token, string Revoke, string Drive, string Upload)
{
    public static readonly GoogleEndpoints Default = new(
        "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", "https://oauth2.googleapis.com/revoke",
        "https://www.googleapis.com/drive/v3", "https://www.googleapis.com/upload/drive/v3");
}

public sealed class GoogleSyncException : Exception
{
    /// <summary>true = precisa conectar de novo (token expirou ou foi revogado).</summary>
    public bool NeedsLogin { get; }
    public GoogleSyncException(string message, bool needsLogin = false) : base(message) { NeedsLogin = needsLogin; }
}

/// <summary>Perfil guardado na pasta oculta do aplicativo no Drive (um por computador).</summary>
public sealed record CloudProfile(string FileId, string MachineId, string Computer, DateTime Modified, long Size);

public static class Pkce
{
    /// <summary>Texto aleatório seguro para URL (base64url, sem preenchimento).</summary>
    public static string RandomUrlSafe(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    /// <summary>Par (verifier, challenge) do PKCE com S256.</summary>
    public static (string Verifier, string Challenge) Create()
    {
        string verifier = RandomUrlSafe(48);   // 64 caracteres, dentro do limite de 43 a 128
        return (verifier, Challenge(verifier));
    }

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}

/// <summary>
/// Sincroniza o perfil com a conta Google do usuário, na pasta oculta do aplicativo no Drive (escopo drive.appdata: o programa
/// não enxerga os outros arquivos). Login pelo navegador (OAuth para apps de computador, com PKCE e retorno em 127.0.0.1).
/// Só envia ou baixa quando o usuário pede. Os tokens ficam cifrados com DPAPI nas configurações.
/// </summary>
public sealed class GoogleSyncService
{
    public const string DriveScope = "https://www.googleapis.com/auth/drive.appdata";
    public const string Scope = "openid email " + DriveScope;
    public const string MissingScopeMessage = "Faltou a permissão do Google Drive. Na tela do Google, deixe marcada a caixa \"Ver e gerenciar os dados de configuração do app no Drive\" e conecte de novo.";
    /// <summary>ID de cliente OAuth embutido pelo mantenedor ao compilar (vazio = o usuário informa o dele nas configurações).</summary>
    public const string BuiltInClientId = "";
    public const string BuiltInClientSecret = "";
    private const string FilePrefix = "perfil-", FileSuffix = ".pmpperfil";

    private readonly AppSettings _s;
    private readonly HttpClient _http;
    private readonly GoogleEndpoints _ep;
    private readonly Func<string, Task> _openBrowser;
    private string? _access;
    private DateTime _accessExpires;

    public GoogleSyncService(AppSettings settings, HttpClient? http = null, GoogleEndpoints? endpoints = null, Func<string, Task>? openBrowser = null)
    {
        _s = settings;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _ep = endpoints ?? GoogleEndpoints.Default;
        _openBrowser = openBrowser ?? (url => { ProcessLauncher.OpenUriDeElevated(url); return Task.CompletedTask; });
    }

    public bool IsConnected => !string.IsNullOrEmpty(_s.GoogleRefreshToken);
    public string? Email => _s.GoogleEmail;
    public string? ClientId => string.IsNullOrWhiteSpace(_s.GoogleClientId) ? (BuiltInClientId == "" ? null : BuiltInClientId) : _s.GoogleClientId.Trim();
    private string? ClientSecret => string.IsNullOrWhiteSpace(_s.GoogleClientSecret) ? (BuiltInClientSecret == "" ? null : BuiltInClientSecret) : _s.GoogleClientSecret.Trim();
    public bool HasClient => ClientId != null;

    public static string FileNameFor(string machineId) => FilePrefix + machineId + FileSuffix;

    // ---------------------------------------------------------------- login

    /// <summary>Abre o navegador para o usuário entrar e autorizar; guarda o token de atualização e devolve o e-mail da conta.</summary>
    public async Task<string?> LoginAsync(CancellationToken ct, TimeSpan? timeout = null)
    {
        if (ClientId is not { } clientId)
            throw new GoogleSyncException("Falta o ID de cliente do Google. Veja docs/GOOGLE.md e preencha em Configuração de acesso.");
        var (verifier, challenge) = Pkce.Create();
        string state = Pkce.RandomUrlSafe(16);
        int port = FreeLoopbackPort();
        string redirect = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        string url = _ep.Auth + "?" + Query(("client_id", clientId), ("redirect_uri", redirect), ("response_type", "code"), ("scope", Scope),
            ("code_challenge", challenge), ("code_challenge_method", "S256"), ("state", state), ("access_type", "offline"), ("prompt", "consent"));
        // Uma única fonte de resposta: a volta do navegador, um erro ao abrir o navegador ou o tempo esgotado.
        var got = new TaskCompletionSource<HttpListenerContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = listener.GetContextAsync().ContinueWith(task =>
        {
            if (task.IsFaulted) got.TrySetException(task.Exception!.InnerException!);
            else if (task.IsCanceled) got.TrySetCanceled();
            else got.TrySetResult(task.Result);
        });
        _ = Task.Run(async () =>
        {
            try { await _openBrowser(url); }
            catch (Exception ex) { got.TrySetException(new GoogleSyncException("Não foi possível abrir o navegador: " + ex.Message)); }
        });
        var limit = Task.Delay(timeout ?? TimeSpan.FromMinutes(3), ct);
        if (await Task.WhenAny(got.Task, limit) != got.Task)
        {
            ct.ThrowIfCancellationRequested();
            throw new GoogleSyncException("O login não foi concluído a tempo. Tente de novo.");
        }
        var ctx = await got.Task;
        string? code = ctx.Request.QueryString["code"], err = ctx.Request.QueryString["error"], gotState = ctx.Request.QueryString["state"];
        bool ok = err == null && code != null && gotState == state;
        await Reply(ctx, ok ? "Pronto! Você já pode fechar esta janela e voltar ao PC Monitor Pro."
                            : "Não foi possível concluir o login. Volte ao PC Monitor Pro e tente de novo.");
        if (err != null)
            throw new GoogleSyncException(err == "access_denied" ? "Você não autorizou o acesso." : "O Google recusou o login (" + err + ").");
        if (!ok) throw new GoogleSyncException("Resposta de login inválida (estado diferente). Nada foi guardado.");

        var json = await PostForm(_ep.Token, new[]
        {
            ("code", code!), ("client_id", clientId), ("client_secret", ClientSecret ?? ""), ("code_verifier", verifier),
            ("redirect_uri", redirect), ("grant_type", "authorization_code"),
        }, ct);
        // O Google deixa o usuário desmarcar permissões na tela de consentimento: confira o que foi realmente concedido.
        string granted = json["scope"]?.GetValue<string>() ?? "";
        if (!granted.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(DriveScope))
        {
            await RevokeQuietly(json["refresh_token"]?.GetValue<string>() ?? json["access_token"]?.GetValue<string>());
            throw new GoogleSyncException(MissingScopeMessage);
        }
        string? refresh = json["refresh_token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(refresh))
            throw new GoogleSyncException("O Google não devolveu a permissão de uso contínuo. Remova o acesso do app em myaccount.google.com/permissions e tente de novo.");
        SetAccess(json);
        _s.GoogleRefreshToken = refresh;
        _s.GoogleEmail = EmailFromIdToken(json["id_token"]?.GetValue<string>());
        _s.Save();
        return _s.GoogleEmail;
    }

    /// <summary>Revoga a permissão no Google (se der) e apaga os tokens daqui.</summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        var token = _s.GoogleRefreshToken;
        _s.GoogleRefreshToken = null; _s.GoogleEmail = null; _access = null;
        _s.Save();
        if (string.IsNullOrEmpty(token)) return;
        try { using var r = await _http.PostAsync(_ep.Revoke, new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("token", token) }), ct); }
        catch (Exception ex) { AppLog.Debug(ex); }   // sem rede: os tokens locais já foram apagados
    }

    private async Task RevokeQuietly(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        try { using var r = await _http.PostAsync(_ep.Revoke, new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("token", token) })); }
        catch (Exception ex) { AppLog.Debug(ex); }
    }

    // ---------------------------------------------------------------- perfis na nuvem

    public async Task<List<CloudProfile>> ListAsync(CancellationToken ct = default)
    {
        var res = new List<CloudProfile>();
        string? page = null;
        do
        {
            string q = Query(("spaces", "appDataFolder"), ("pageSize", "100"), ("fields", "nextPageToken,files(id,name,modifiedTime,size,appProperties)"),
                ("q", $"name contains '{FilePrefix}' and trashed = false"), ("pageToken", page));
            using var r = await Send(() => new HttpRequestMessage(HttpMethod.Get, $"{_ep.Drive}/files?{q}"), ct);
            var j = JsonNode.Parse(await r.Content.ReadAsStringAsync(ct)) as JsonObject ?? throw new GoogleSyncException("Resposta inesperada do Google Drive.");
            foreach (var f in j["files"] as JsonArray ?? new JsonArray())
            {
                string name = f?["name"]?.GetValue<string>() ?? "";
                if (!name.StartsWith(FilePrefix) || !name.EndsWith(FileSuffix)) continue;
                string machine = name[FilePrefix.Length..^FileSuffix.Length];
                DateTime.TryParse(f?["modifiedTime"]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var mod);
                long.TryParse(f?["size"]?.GetValue<string>(), out var size);
                res.Add(new CloudProfile(f!["id"]!.GetValue<string>(), machine, f["appProperties"]?["computer"]?.GetValue<string>() ?? "(sem nome)", mod.ToLocalTime(), size));
            }
            page = j["nextPageToken"]?.GetValue<string>();
        } while (page != null);
        return res.OrderByDescending(p => p.Modified).ToList();
    }

    /// <summary>Cria ou atualiza o perfil deste computador (arquivo "perfil-&lt;id&gt;.pmpperfil").</summary>
    public async Task UploadAsync(string profileJson, string computerName, CancellationToken ct = default)
    {
        if (profileJson.Length > ProfileTransfer.MaxBytes) throw new GoogleSyncException("O perfil é grande demais para enviar.");
        string machine = _s.EnsureMachineId();
        var existing = (await ListAsync(ct)).FirstOrDefault(p => p.MachineId == machine);
        var meta = new JsonObject { ["appProperties"] = new JsonObject { ["computer"] = computerName, ["app"] = "PC Monitor Pro" } };
        if (existing == null) { meta["name"] = FileNameFor(machine); meta["parents"] = new JsonArray("appDataFolder"); }
        (HttpMethod method, string url) = existing == null
            ? (HttpMethod.Post, $"{_ep.Upload}/files?uploadType=multipart")
            : (HttpMethod.Patch, $"{_ep.Upload}/files/{existing.FileId}?uploadType=multipart");
        using var r = await Send(() =>
        {
            var body = new MultipartContent("related", "pmp" + Pkce.RandomUrlSafe(9));
            body.Add(new StringContent(meta.ToJsonString(), Encoding.UTF8, "application/json"));
            body.Add(new StringContent(profileJson, Encoding.UTF8, "application/json"));
            return new HttpRequestMessage(method, url) { Content = body };
        }, ct);
    }

    public async Task<string> DownloadAsync(CloudProfile p, CancellationToken ct = default)
    {
        using var r = await Send(() => new HttpRequestMessage(HttpMethod.Get, $"{_ep.Drive}/files/{Uri.EscapeDataString(p.FileId)}?alt=media"), ct);
        if (r.Content.Headers.ContentLength is > ProfileTransfer.MaxBytes) throw new GoogleSyncException("O perfil na nuvem é grande demais.");
        string text = await r.Content.ReadAsStringAsync(ct);
        if (text.Length > ProfileTransfer.MaxBytes) throw new GoogleSyncException("O perfil na nuvem é grande demais.");
        return text;
    }

    /// <summary>Apaga da nuvem o perfil deste computador. Devolve false se não havia.</summary>
    public async Task<bool> DeleteMineAsync(CancellationToken ct = default)
    {
        string machine = _s.EnsureMachineId();
        var mine = (await ListAsync(ct)).FirstOrDefault(p => p.MachineId == machine);
        if (mine == null) return false;
        using var r = await Send(() => new HttpRequestMessage(HttpMethod.Delete, $"{_ep.Drive}/files/{Uri.EscapeDataString(mine.FileId)}"), ct);
        return true;
    }

    // ---------------------------------------------------------------- HTTP e tokens

    /// <summary>Envia com o token; se o Google disser 401, renova o token e tenta mais uma vez.</summary>
    private async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            using var req = make();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
            HttpResponseMessage res;
            try { res = await _http.SendAsync(req, ct); }
            catch (HttpRequestException ex) { throw new GoogleSyncException("Sem conexão com o Google: " + ex.Message); }
            if (res.IsSuccessStatusCode) return res;
            if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { res.Dispose(); _access = null; continue; }
            string detail = await ErrorText(res);
            res.Dispose();
            if (res.StatusCode == HttpStatusCode.Forbidden && detail.Contains("insufficient", StringComparison.OrdinalIgnoreCase))
            {
                await RevokeQuietly(_s.GoogleRefreshToken);
                _s.GoogleRefreshToken = null; _s.GoogleEmail = null; _access = null; _s.Save();   // essa conexão não serve: recomeçar
                throw new GoogleSyncException(MissingScopeMessage, needsLogin: true);
            }
            throw new GoogleSyncException(res.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "O Google recusou o acesso. Conecte de novo.",
                HttpStatusCode.Forbidden => "O Google Drive negou a operação" + (detail != "" ? $": {detail}" : ". Confira se a Google Drive API está ativada no seu projeto."),
                HttpStatusCode.NotFound => "O arquivo não existe mais na nuvem.",
                _ => $"Erro do Google ({(int)res.StatusCode})" + (detail != "" ? $": {detail}" : ""),
            }, needsLogin: res.StatusCode == HttpStatusCode.Unauthorized);
        }
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (_access != null && DateTime.UtcNow < _accessExpires - TimeSpan.FromSeconds(60)) return _access;
        var refresh = _s.GoogleRefreshToken;
        if (string.IsNullOrEmpty(refresh) || ClientId is not { } cid) throw new GoogleSyncException("Conecte sua conta do Google primeiro.", needsLogin: true);
        try
        {
            var j = await PostForm(_ep.Token, new[]
            {
                ("client_id", cid), ("client_secret", ClientSecret ?? ""), ("refresh_token", refresh), ("grant_type", "refresh_token"),
            }, ct);
            SetAccess(j);
            return _access!;
        }
        catch (GoogleSyncException ex) when (ex.NeedsLogin)
        {
            _s.GoogleRefreshToken = null; _s.GoogleEmail = null; _access = null; _s.Save();   // permissão revogada ou expirada
            throw;
        }
    }

    private void SetAccess(JsonObject j)
    {
        _access = j["access_token"]?.GetValue<string>() ?? throw new GoogleSyncException("O Google não devolveu o token de acesso.");
        int secs = j["expires_in"]?.GetValue<int>() ?? 3600;
        _accessExpires = DateTime.UtcNow.AddSeconds(secs);
    }

    private async Task<JsonObject> PostForm(string url, (string, string)[] fields, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await _http.PostAsync(url, new FormUrlEncodedContent(fields.Where(f => f.Item2 != "" || f.Item1 != "client_secret")
                .Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2))), ct);
        }
        catch (HttpRequestException ex) { throw new GoogleSyncException("Sem conexão com o Google: " + ex.Message); }
        using (res)
        {
            string text = await res.Content.ReadAsStringAsync(ct);
            JsonObject? j = null;
            try { j = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }
            if (res.IsSuccessStatusCode && j != null) return j;
            string error = j?["error"]?.GetValue<string>() ?? "";
            if (error == "invalid_grant") throw new GoogleSyncException("A permissão expirou ou foi removida. Conecte de novo.", needsLogin: true);
            throw new GoogleSyncException($"O Google recusou o pedido ({(int)res.StatusCode} {error}): {j?["error_description"]?.GetValue<string>()}".TrimEnd(' ', ':'));
        }
    }

    // ---------------------------------------------------------------- utilidades

    private static async Task<string> ErrorText(HttpResponseMessage r)
    {
        try { return (JsonNode.Parse(await r.Content.ReadAsStringAsync()) as JsonObject)?["error"]?["message"]?.GetValue<string>() ?? ""; }
        catch { return ""; }
    }

    private static string Query(params (string Key, string? Value)[] pairs) =>
        string.Join("&", pairs.Where(p => p.Value != null).Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));

    private static int FreeLoopbackPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; } finally { l.Stop(); }
    }

    private static async Task Reply(HttpListenerContext ctx, string message)
    {
        byte[] body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=\"utf-8\"><title>PC Monitor Pro</title><body style=\"font-family:Segoe UI,sans-serif;background:#111;color:#eee;display:grid;place-items:center;height:100vh;margin:0\"><p style=\"font-size:18px\">{WebUtility.HtmlEncode(message)}</p></body>");
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = body.Length;
        await ctx.Response.OutputStream.WriteAsync(body);
        ctx.Response.Close();
    }

    /// <summary>E-mail do id_token. Só para mostrar na tela: veio direto do Google por HTTPS, então a assinatura não é verificada aqui.</summary>
    public static string? EmailFromIdToken(string? idToken)
    {
        try
        {
            var parts = idToken?.Split('.');
            if (parts is not { Length: 3 }) return null;
            return (JsonNode.Parse(Encoding.UTF8.GetString(Pkce.FromBase64Url(parts[1]))) as JsonObject)?["email"]?.GetValue<string>();
        }
        catch (Exception ex) { AppLog.Debug(ex); return null; }
    }
}
