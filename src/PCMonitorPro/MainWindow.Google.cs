using System.Windows;
using System.Windows.Controls;

namespace PCMonitorPro;

/// <summary>Sincronização opcional do perfil com o Google Drive (pasta oculta do app). Tudo manual: nada é enviado sem clique.</summary>
public partial class MainWindow
{
    private GoogleSyncService? _google;
    private CancellationTokenSource? _googleCts;
    private bool _googleBusy;
    private GoogleSyncService Google => _google ??= new GoogleSyncService(_settings);

    private const string GoogleGuideUrl = "https://github.com/rodrigogus94/PCMonitorPro/blob/main/docs/GOOGLE.md";

    private void RefreshGoogleUi(string? message = null)
    {
        bool has = Google.HasClient, connected = Google.IsConnected;
        GoogleStatus.Text = message ?? (!has ? "Não configurado. Siga o guia e preencha o ID do cliente abaixo."
            : connected ? $"Conectado como {Google.Email ?? "sua conta"}."
            : "Pronto para conectar.");
        // durante o login o botão vira "CANCELAR"
        GoogleConnectBtn.Content = _googleBusy && _googleCts != null && !connected ? "CANCELAR" : "CONECTAR";
        GoogleConnectBtn.IsEnabled = has && !connected && (!_googleBusy || _googleCts != null);
        foreach (var b in new[] { GoogleUploadBtn, GoogleDownloadBtn, GoogleDeleteBtn, GoogleDisconnectBtn }) b.IsEnabled = connected && !_googleBusy;

        _setLoading = true;
        GoogleClientIdBox.Text = _settings.GoogleClientId ?? "";
        GoogleClientSecretBox.ToolTip = string.IsNullOrEmpty(_settings.GoogleClientSecretProtected) ? "Segredo do cliente" : "Segredo salvo (cifrado). Digite para trocar.";
        _setLoading = false;
    }

    private void GoogleClient_Changed(object sender, RoutedEventArgs e)
    {
        if (_setLoading) return;
        string id = GoogleClientIdBox.Text.Trim();
        bool changed = id != (_settings.GoogleClientId ?? "");
        _settings.GoogleClientId = id == "" ? null : id;
        if (GoogleClientSecretBox.Password != "") { _settings.GoogleClientSecret = GoogleClientSecretBox.Password; changed = true; GoogleClientSecretBox.Password = ""; }
        if (!changed) return;
        // trocar as credenciais invalida a conexão atual (o token pertence ao cliente antigo)
        _settings.GoogleRefreshToken = null; _settings.GoogleEmail = null;
        _settings.Save();
        RefreshGoogleUi();
    }

    private void BtnGoogleGuide_Click(object sender, RoutedEventArgs e)
    {
        try { ProcessLauncher.OpenUriDeElevated(GoogleGuideUrl); }
        catch (Exception ex) { AppLog.Debug(ex); }
    }

    /// <summary>Roda uma operação do Google com o botão ocupado e a mensagem de erro na tela.</summary>
    private async Task GoogleRun(string working, Func<CancellationToken, Task<string?>> action)
    {
        if (_googleBusy) return;
        _googleBusy = true; _googleCts = new CancellationTokenSource();
        RefreshGoogleUi(working);
        string? msg = null;
        try { msg = await action(_googleCts.Token); }
        catch (OperationCanceledException) { msg = "Cancelado."; }
        catch (GoogleSyncException ex) { msg = ex.Message; }
        catch (Exception ex) { AppLog.Warn("Google: operação falhou", ex); msg = "Falhou: " + ex.Message; }
        finally { _googleBusy = false; _googleCts.Dispose(); _googleCts = null; }
        RefreshGoogleUi(msg);
    }

    private async void BtnGoogleConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_googleBusy) { _googleCts?.Cancel(); return; }   // o botão vira CANCELAR durante o login
        await GoogleRun("Abrimos o navegador: entre na sua conta e autorize. Aguardando…", async ct =>
        {
            var email = await Google.LoginAsync(ct);
            return $"Conectado como {email ?? "sua conta"}.";
        });
    }

    private async void BtnGoogleUpload_Click(object sender, RoutedEventArgs e)
    {
        var ask = ProfileDialog.Ask(this, "Enviar perfil para o Google", $"Este perfil ficará na sua pasta oculta do Drive com o nome \"{Environment.MachineName}\" e substitui o que este PC já enviou antes.",
            Enum.GetValues<ProfileSection>(), "ENVIAR");
        if (ask is not { } a) return;
        await GoogleRun("Enviando…", async ct =>
        {
            await Google.UploadAsync(ProfileTransfer.Export(_settings, a.Sections, includeComputerName: true), Environment.MachineName, ct);
            return $"Perfil enviado às {DateTime.Now:HH:mm}.";
        });
    }

    private async void BtnGoogleDownload_Click(object sender, RoutedEventArgs e)
    {
        ProfileFile? file = null;
        await GoogleRun("Buscando os perfis na nuvem…", async ct =>
        {
            var list = await Google.ListAsync(ct);
            if (list.Count == 0) return "Nenhum perfil na nuvem ainda. Envie o de um computador primeiro.";
            string mine = _settings.EnsureMachineId();
            int pick = ChoiceDialog.Ask(this, "Baixar perfil", "Escolha o computador cujo perfil você quer trazer para cá.",
                list.Select(p => $"{p.Computer}{(p.MachineId == mine ? " (este PC)" : "")} · enviado em {p.Modified:dd/MM/yyyy HH:mm}").ToList()) ?? -1;
            if (pick < 0) return "Cancelado.";
            file = ProfileTransfer.Read(await Google.DownloadAsync(list[pick], ct));
            return "Perfil baixado.";
        });
        if (file != null) ImportProfile(file);
    }

    private async void BtnGoogleDelete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Apagar da sua conta do Google o perfil enviado por este computador? Os perfis de outros computadores não são afetados.",
                "Google", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await GoogleRun("Apagando…", async ct => await Google.DeleteMineAsync(ct) ? "Perfil deste computador apagado da nuvem." : "Este computador não tinha perfil na nuvem.");
    }

    private async void BtnGoogleDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Desconectar a conta do Google deste programa? Os perfis já enviados continuam na nuvem (apague antes, se quiser removê-los).",
                "Google", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await GoogleRun("Desconectando…", async ct => { await Google.DisconnectAsync(ct); return "Desconectado. A permissão foi removida da sua conta do Google."; });
    }
}
