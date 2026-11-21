using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PCMonitorPro;

/// <summary>
/// Pasta de trabalho que só administradores e o sistema conseguem alterar (%ProgramData%\PCMonitorPro).
/// O programa roda como administrador: arquivos que ele confere (assinatura) e depois executa, instala ou carrega
/// não podem ficar em %TEMP% ou %AppData%, onde qualquer programa comum poderia trocá-los entre a conferência e o uso.
/// </summary>
public static class SecureDir
{
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System_ = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier CreatorOwner = new(WellKnownSidType.CreatorOwnerSid, null);
    // Conta de instalação do Windows (dona de várias pastas de sistema)
    private static readonly SecurityIdentifier TrustedInstaller =
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    internal static string? RootOverride { get; set; }

    public static string Root => RootOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PCMonitorPro");

    /// <summary>Cria (ou recria vazia) a subpasta protegida e devolve o caminho. Falha, em vez de continuar, se a raiz não for segura.</summary>
    public static string Create(string sub, bool wipe = true)
    {
        EnsureRoot();
        string dir = Path.GetFullPath(Path.Combine(Root, sub));
        if (!dir.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Subpasta inválida.");
        if (wipe && Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);   // herda a ACL protegida da raiz
        return dir;
    }

    private static void EnsureRoot()
    {
        var di = new DirectoryInfo(Root);
        if (!di.Exists)
        {
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);   // não herda de ProgramData
            sec.SetOwner(Admins);
            foreach (var sid in new[] { Admins, System_ })
                sec.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            di.Create(sec);
        }
        var reason = Inspect(di.GetAccessControl());
        if (reason != null)
            throw new InvalidOperationException(
                $"A pasta {Root} não é segura ({reason}). Apague-a como administrador e abra o programa de novo.");
    }

    /// <summary>Devolve o motivo se a pasta puder ser alterada por quem não é administrador; null se estiver protegida.</summary>
    internal static string? Inspect(DirectorySecurity sec)
    {
        var owner = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner == null || !(owner.Equals(Admins) || owner.Equals(System_) || owner.Equals(TrustedInstaller)))
            return $"dono inesperado: {owner?.Value ?? "?"}";

        const FileSystemRights writeLike = FileSystemRights.WriteData | FileSystemRights.CreateFiles | FileSystemRights.AppendData |
            FileSystemRights.CreateDirectories | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (r.AccessControlType != AccessControlType.Allow) continue;
            var sid = (SecurityIdentifier)r.IdentityReference;
            if (sid.Equals(Admins) || sid.Equals(System_) || sid.Equals(TrustedInstaller) || sid.Equals(CreatorOwner)) continue;
            if ((r.FileSystemRights & writeLike) != 0)
                return $"{sid.Value} tem permissão de escrita";
        }
        return null;
    }
}
