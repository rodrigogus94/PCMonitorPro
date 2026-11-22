using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PCMonitorPro;

/// <summary>
/// Abre jogos e programas auxiliares SEM privilégio de administrador, mesmo com o PC Monitor Pro elevado. Rodar jogo como
/// administrador amplia o estrago de qualquer falha ou mod malicioso e faz o que o usuário configurou virar execução elevada.
/// Usa o token do Explorer (o usuário comum logado); se não der, abre normalmente e registra um aviso no log.
/// </summary>
public static class ProcessLauncher
{
    public static bool IsElevated()
    {
        try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        catch (Exception ex) { AppLog.Debug(ex); return false; }
    }

    /// <summary>Inicia o programa sem elevação sempre que possível. Devolve null se o Windows só abriu via shell (sem processo).</summary>
    public static Process? StartDeElevated(string file, string args = "", string? workDir = null)
    {
        if (IsElevated())
        {
            try { return StartWithShellToken(file, args, workDir); }
            catch (Exception ex) { AppLog.Warn($"Não consegui abrir {System.IO.Path.GetFileName(file)} sem elevação; abrindo normalmente.", ex); }
        }
        return Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, WorkingDirectory = workDir ?? "" });
    }

    /// <summary>Atalhos de protocolo (steam://, com.epicgames.launcher://...) também saem do Explorer, sem elevação.</summary>
    public static void OpenUriDeElevated(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || u.Scheme is "file" or "javascript")
            throw new InvalidOperationException("Endereço inválido.");
        StartDeElevated("explorer.exe", $"\"{uri}\"");
    }

    // ---------------------------------------------------------------- token do shell
    private const uint TOKEN_DUPLICATE = 0x0002, TOKEN_ALL_ACCESS = 0xF01FF, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr proc, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attrs, int impLevel, int tokenType, out IntPtr newToken);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string? app, string cmd, int flags, IntPtr env,
                                                       string? cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    private static Process? StartWithShellToken(string file, string args, string? workDir)
    {
        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero) throw new InvalidOperationException("O Explorer não está em execução.");
        GetWindowThreadProcessId(shell, out uint pid);
        IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid), hTok = IntPtr.Zero, hPrimary = IntPtr.Zero;
        if (hProc == IntPtr.Zero) throw new Win32Exception();
        try
        {
            if (!OpenProcessToken(hProc, TOKEN_DUPLICATE, out hTok)) throw new Win32Exception();
            // 2 = SecurityImpersonation, 1 = TokenPrimary
            if (!DuplicateTokenEx(hTok, TOKEN_ALL_ACCESS, IntPtr.Zero, 2, 1, out hPrimary)) throw new Win32Exception();
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            string cmd = $"\"{file}\"" + (string.IsNullOrWhiteSpace(args) ? "" : " " + args);
            string? cwd = string.IsNullOrEmpty(workDir) ? null : workDir;
            if (!CreateProcessWithTokenW(hPrimary, 0, null, cmd, 0, IntPtr.Zero, cwd, ref si, out var pi)) throw new Win32Exception();
            CloseHandle(pi.hThread);
            try
            {
                // O Process do .NET precisa do próprio handle ANTES de soltarmos o nosso. Se o programa terminar logo
                // (comandos rápidos), abrir o handle depois deixa ExitCode ilegível ("não foi iniciado por este objeto").
                var p = Process.GetProcessById(pi.dwProcessId);
                _ = p.SafeHandle;
                return p;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                AppLog.Debug(ex);
                return null;
            }
            finally { CloseHandle(pi.hProcess); }
        }
        finally
        {
            if (hPrimary != IntPtr.Zero) CloseHandle(hPrimary);
            if (hTok != IntPtr.Zero) CloseHandle(hTok);
            CloseHandle(hProc);
        }
    }
}
