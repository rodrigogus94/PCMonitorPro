using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace PCMonitorPro;

/// <summary>Confere de quem é um arquivo baixado (assinatura Authenticode válida E empresa exata) e de onde ele veio (domínio).</summary>
public static class TrustedPublisher
{
    public static readonly string[] NvidiaOrgs = { "NVIDIA Corporation" };
    public static readonly string[] AmdOrgs = { "Advanced Micro Devices, Inc." };
    public static readonly string[] CorsairOrgs = { "Corsair Memory, Inc.", "Corsair Gaming, Inc." };

    public static readonly string[] NvidiaHosts = { "nvidia.com", "download.nvidia.com", "us.download.nvidia.com" };
    public static readonly string[] AmdHosts = { "amd.com" };
    public static readonly string[] CorsairHosts = { "github.com", "githubusercontent.com" };

    /// <summary>Só https, e host igual ao permitido ou subdomínio dele ("evil-nvidia.com" e "nvidia.com.evil.io" não passam).</summary>
    public static bool IsAllowedHost(string url, IEnumerable<string> allowedDomains)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        string host = u.IdnHost.TrimEnd('.').ToLowerInvariant();
        return allowedDomains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    /// <summary>Campo O (empresa) do certificado: comparação exata, sem "contém".</summary>
    public static bool OrgMatches(string subject, IEnumerable<string> allowedOrgs)
    {
        string? org = OrganizationOf(subject);
        return org != null && allowedOrgs.Any(o => o.Equals(org, StringComparison.OrdinalIgnoreCase));
    }

    internal static string? OrganizationOf(string subject)
    {
        try
        {
            var dn = new X500DistinguishedName(subject);
            foreach (var rdn in dn.EnumerateRelativeDistinguishedNames())
                if (rdn.GetSingleElementType().Value == "2.5.4.10")   // OID de "O"
                    return rdn.GetSingleElementValue();
        }
        catch (System.Security.Cryptography.CryptographicException) { }   // assunto malformado: não confia
        return null;
    }

    /// <summary>
    /// Confere o arquivo: assinatura Authenticode válida pelo Windows e empresa na lista. Lança exceção (sem apagar o arquivo)
    /// se algo não bater; o chamador decide o que fazer. Devolve o nome do assinante.
    /// </summary>
    public static string RequireSigner(string path, IEnumerable<string> allowedOrgs)
    {
        int trust = CorsairSdkInstaller.WinVerifyTrustFile(path);
        if (trust == unchecked((int)0x800B0100)) throw new InvalidOperationException("O arquivo não tem assinatura digital.");
        if (trust != 0) throw new InvalidOperationException($"Assinatura digital inválida (0x{trust:X8}).");
        string subject;
#pragma warning disable SYSLIB0057
        using (var cert = X509Certificate.CreateFromSignedFile(path))
            subject = new X509Certificate2(cert).Subject;
#pragma warning restore SYSLIB0057
        if (!OrgMatches(subject, allowedOrgs))
            throw new InvalidOperationException($"O arquivo está assinado por outra empresa ({subject}).");
        return OrganizationOf(subject) ?? subject;
    }

    /// <summary>Abre o arquivo bloqueando alteração/troca enquanto durar (use durante conferir + carregar/executar).</summary>
    public static FileStream LockForRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
}
