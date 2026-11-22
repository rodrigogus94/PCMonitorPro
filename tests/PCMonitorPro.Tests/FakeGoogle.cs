using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace PCMonitorPro.Tests;

/// <summary>Servidor local que imita o OAuth do Google e a API do Drive (appDataFolder) só o suficiente para testar o cliente.</summary>
public sealed class FakeGoogle : IDisposable
{
    public sealed class FakeFile
    {
        public string Id = "", Name = "", Content = "", Computer = "";
        public DateTime Modified = DateTime.UtcNow;
    }

    private readonly HttpListener _l = new();
    private int _atCounter;
    private string? _challenge, _redirect, _state;

    public string Base { get; }
    public string ClientId = "cid-teste", ClientSecret = "segredo-teste", RefreshToken = "RT-TESTE";
    public string CurrentAccessToken = "AT-0";
    public string Email = "pessoa@exemplo.com";
    public bool InvalidGrant, ForbidDrive, InsufficientScope;
    /// <summary>Escopos que o "usuário" deixou marcados na tela do Google (null = todos os pedidos).</summary>
    public string? GrantedScope;
    public int PageSize;
    public int TokenCalls, RefreshCalls;
    public bool Revoked;
    public string? LastScope;
    public readonly Dictionary<string, FakeFile> Files = new();

    public GoogleEndpoints Endpoints => new(Base + "/auth", Base + "/token", Base + "/revoke", Base + "/drive/v3", Base + "/upload/drive/v3");

    public FakeGoogle()
    {
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
        Base = $"http://127.0.0.1:{port}";
        _l.Prefixes.Add(Base + "/"); _l.Start();
        _ = Task.Run(Loop);
    }

    /// <summary>O token de acesso atual deixa de valer (o próximo pedido leva 401 e o cliente precisa renovar).</summary>
    public void ExpireAccessToken() => CurrentAccessToken = "AT-expirado";

    private async Task Loop()
    {
        while (_l.IsListening)
        {
            HttpListenerContext c;
            try { c = await _l.GetContextAsync(); } catch { return; }
            try { Handle(c); } catch (Exception ex) { Send(c, 500, "text/plain", ex.ToString()); }
        }
    }

    private static void Send(HttpListenerContext c, int status, string type, string body)
    {
        var b = Encoding.UTF8.GetBytes(body);
        c.Response.StatusCode = status; c.Response.ContentType = type + "; charset=utf-8"; c.Response.ContentLength64 = b.Length;
        c.Response.OutputStream.Write(b); c.Response.Close();
    }

    private static void Json(HttpListenerContext c, int status, JsonNode n) => Send(c, status, "application/json", n.ToJsonString());

    private void Handle(HttpListenerContext c)
    {
        string path = c.Request.Url!.AbsolutePath, method = c.Request.HttpMethod;
        var q = HttpUtility.ParseQueryString(c.Request.Url.Query);

        if (path == "/auth")
        {
            if (q["client_id"] != ClientId || q["response_type"] != "code" || q["code_challenge_method"] != "S256" || string.IsNullOrEmpty(q["code_challenge"]))
            { Send(c, 400, "text/plain", "pedido de login inválido"); return; }
            LastScope = q["scope"];
            _challenge = q["code_challenge"]; _redirect = q["redirect_uri"]; _state = q["state"];
            c.Response.StatusCode = 302;
            c.Response.RedirectLocation = $"{_redirect}?code=CODE-1&state={Uri.EscapeDataString(_state!)}";
            c.Response.Close();
            return;
        }
        if (path == "/token" && method == "POST")
        {
            TokenCalls++;
            var f = HttpUtility.ParseQueryString(new StreamReader(c.Request.InputStream).ReadToEnd());
            if (f["client_id"] != ClientId || f["client_secret"] != ClientSecret) { Json(c, 401, new JsonObject { ["error"] = "invalid_client" }); return; }
            if (f["grant_type"] == "authorization_code")
            {
                bool ok = f["code"] == "CODE-1" && f["redirect_uri"] == _redirect
                    && f["code_verifier"] != null && Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(f["code_verifier"]!))).TrimEnd('=').Replace('+', '-').Replace('/', '_') == _challenge;
                if (!ok) { Json(c, 400, new JsonObject { ["error"] = "invalid_grant", ["error_description"] = "código ou verificador inválido" }); return; }
                CurrentAccessToken = "AT-" + (++_atCounter);
                string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(new JsonObject { ["email"] = Email }.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                Json(c, 200, new JsonObject { ["access_token"] = CurrentAccessToken, ["expires_in"] = 3600, ["refresh_token"] = RefreshToken, ["id_token"] = $"x.{payload}.y", ["scope"] = GrantedScope ?? LastScope });
                return;
            }
            if (f["grant_type"] == "refresh_token")
            {
                RefreshCalls++;
                if (InvalidGrant || f["refresh_token"] != RefreshToken) { Json(c, 400, new JsonObject { ["error"] = "invalid_grant" }); return; }
                CurrentAccessToken = "AT-" + (++_atCounter);
                Json(c, 200, new JsonObject { ["access_token"] = CurrentAccessToken, ["expires_in"] = 3600 });
                return;
            }
            Json(c, 400, new JsonObject { ["error"] = "unsupported_grant_type" });
            return;
        }
        if (path == "/revoke" && method == "POST") { Revoked = true; Send(c, 200, "application/json", "{}"); return; }

        // ---- Drive
        if (c.Request.Headers["Authorization"] != "Bearer " + CurrentAccessToken) { Json(c, 401, new JsonObject { ["error"] = new JsonObject { ["message"] = "token inválido" } }); return; }
        if (InsufficientScope) { Json(c, 403, new JsonObject { ["error"] = new JsonObject { ["message"] = "Request had insufficient authentication scopes." } }); return; }
        if (ForbidDrive) { Json(c, 403, new JsonObject { ["error"] = new JsonObject { ["message"] = "Google Drive API has not been used in project" } }); return; }

        if (path == "/drive/v3/files" && method == "GET")
        {
            if (q["spaces"] != "appDataFolder") { Send(c, 400, "text/plain", "falta spaces=appDataFolder"); return; }
            var all = Files.Values.OrderBy(x => x.Name).ToList();
            int start = int.TryParse(q["pageToken"], out var s) ? s : 0, take = PageSize > 0 ? PageSize : all.Count;
            var arr = new JsonArray();
            foreach (var f in all.Skip(start).Take(take))
                arr.Add(new JsonObject
                {
                    ["id"] = f.Id, ["name"] = f.Name, ["modifiedTime"] = f.Modified.ToString("o"), ["size"] = f.Content.Length.ToString(),
                    ["appProperties"] = new JsonObject { ["computer"] = f.Computer },
                });
            var res = new JsonObject { ["files"] = arr };
            if (start + take < all.Count) res["nextPageToken"] = (start + take).ToString();
            Json(c, 200, res);
            return;
        }
        if (path.StartsWith("/drive/v3/files/"))
        {
            string id = path["/drive/v3/files/".Length..];
            if (!Files.TryGetValue(id, out var f)) { Json(c, 404, new JsonObject { ["error"] = new JsonObject { ["message"] = "não encontrado" } }); return; }
            if (method == "GET" && q["alt"] == "media") { Send(c, 200, "application/json", f.Content); return; }
            if (method == "DELETE") { Files.Remove(id); c.Response.StatusCode = 204; c.Response.Close(); return; }
        }
        if (path.StartsWith("/upload/drive/v3/files") && q["uploadType"] == "multipart")
        {
            string ct = c.Request.ContentType ?? "";
            if (!ct.StartsWith("multipart/related")) { Send(c, 400, "text/plain", "esperava multipart/related"); return; }
            string boundary = ct[(ct.IndexOf("boundary=", StringComparison.Ordinal) + 9)..].Trim('"', ' ');
            string body = new StreamReader(c.Request.InputStream, Encoding.UTF8).ReadToEnd();
            var parts = body.Split("--" + boundary).Select(p => p.Trim('\r', '\n')).Where(p => p != "" && p != "--").ToList();
            string Content(string part) => part[(part.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..];
            var meta = JsonNode.Parse(Content(parts[0]))!.AsObject();
            string media = Content(parts[1]);
            FakeFile file;
            if (method == "POST")
            {
                if (meta["parents"]?[0]?.GetValue<string>() != "appDataFolder") { Send(c, 400, "text/plain", "falta parents=appDataFolder"); return; }
                file = new FakeFile { Id = "F" + (Files.Count + 1) + Guid.NewGuid().ToString("N")[..4], Name = meta["name"]!.GetValue<string>() };
                Files[file.Id] = file;
            }
            else
            {
                string id = path[(path.LastIndexOf('/') + 1)..];
                if (!Files.TryGetValue(id, out file!)) { Send(c, 404, "text/plain", "sem arquivo"); return; }
            }
            file.Content = media; file.Modified = DateTime.UtcNow;
            file.Computer = meta["appProperties"]?["computer"]?.GetValue<string>() ?? file.Computer;
            Json(c, 200, new JsonObject { ["id"] = file.Id, ["name"] = file.Name });
            return;
        }
        Send(c, 404, "text/plain", "rota não prevista: " + method + " " + path);
    }

    public void Dispose() { try { _l.Stop(); _l.Close(); } catch { } }
}
