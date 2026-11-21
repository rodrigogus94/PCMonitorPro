using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace PCMonitorPro;

/// <summary>
/// Coleta do diagnóstico do Windows via PowerShell (eventos, discos, rede, desempenho, segurança...). O script vai por stdin,
/// sem arquivo em %TEMP%: evita que outro processo do usuário troque o arquivo antes da execução (o programa roda como administrador).
/// </summary>
public static class DiagnosticsCollector
{
    private static Process? _active;

    public static string PowerShellExe =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Executa a coleta e devolve o JSON bruto. Lança InvalidOperationException se falhar ou passar de 5 minutos.</summary>
    public static async Task<JsonNode> CollectAsync(CancellationToken ct = default, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo(PowerShellExe, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new ASCIIEncoding(), // o script é ASCII puro; sem isso a codificação vem do console (que não existe num app WPF)
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell.");
        _active = proc;
        try
        {
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            // Em modo stdin o PowerShell descarta o script em silêncio se ele não terminar com linha em branco (\n + \r\n, como no original).
            await proc.StandardInput.WriteAsync(Script + "\n\r\n");
            proc.StandardInput.Close();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(5));
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch (Exception ex) { AppLog.Warn("Diagnóstico: falha ao encerrar o PowerShell", ex); }
                if (ct.IsCancellationRequested) throw;
                throw new InvalidOperationException("A coleta do PowerShell passou de 5 minutos e foi cancelada.");
            }
            string output = await outTask, err = await errTask;
            return ParseOutput(output, err, proc.ExitCode);
        }
        finally { _active = null; }
    }

    /// <summary>Extrai o objeto JSON da saída do PowerShell (ignora texto solto antes/depois).</summary>
    public static JsonNode ParseOutput(string output, string stderr = "", int? exitCode = null)
    {
        int i = output.IndexOf('{'), j = output.LastIndexOf('}');
        if (i < 0 || j <= i)
        {
            var e = stderr.Trim(); if (e.Length > 600) e = e[..600];
            throw new InvalidOperationException($"O PowerShell não devolveu dados (saída {output.Length} caracteres, código {(exitCode?.ToString() ?? "?")}). " + e);
        }
        try { return JsonNode.Parse(output[i..(j + 1)]) ?? throw new InvalidOperationException("Resposta vazia do PowerShell."); }
        catch (System.Text.Json.JsonException ex) { throw new InvalidOperationException("Resposta inválida do PowerShell: " + ex.Message); }
    }

    /// <summary>Cancela a coleta em andamento (chamado ao fechar o programa).</summary>
    public static void Cancel()
    {
        var p = _active;
        if (p == null) return;
        try { p.Kill(true); } catch (Exception ex) { AppLog.Warn("Diagnóstico: falha ao cancelar", ex); }
    }

    public const string Script = """
$ErrorActionPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$start = (Get-Date).AddDays(-30)
$start90 = (Get-Date).AddDays(-90)
$r = [ordered]@{}
$script:errs = @()
function D($x) { if ($x) { $x.ToString('s') } else { $null } }
function Sec($name, [scriptblock]$sb) {
  try { $r[$name] = & $sb } catch { $script:errs += ($name + ': ' + $_.Exception.Message) }
}
function SecL($name, [scriptblock]$sb) {
  try { $r[$name] = @(& $sb) } catch { $script:errs += ($name + ': ' + $_.Exception.Message) }
}
function Ev($filter, $n) {
  Get-WinEvent -FilterHashtable $filter -MaxEvents $n -ErrorAction SilentlyContinue | ForEach-Object {
    $m = ("" + $_.Message) -replace '\s+', ' '
    if ($m.Length -gt 400) { $m = $m.Substring(0, 400) }
    [pscustomobject]@{ t = (D $_.TimeCreated); id = $_.Id; p = $_.ProviderName; m = $m }
  }
}

Sec 'os' {
  $o = Get-CimInstance Win32_OperatingSystem
  $cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
  $sh = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\WindowsSelfHost\Applicability'
  [pscustomobject]@{ caption = $o.Caption; build = "$($o.BuildNumber)"; version = $o.Version; display = $cv.DisplayVersion
    ubr = $cv.UBR; boot = (D $o.LastBootUpTime); branch = $sh.BranchName; ring = $sh.Ring; content = $sh.ContentType
    flight = "$($sh.IsBuildFlightingEnabled)"; ramKb = $o.TotalVisibleMemorySize; ramFreeKb = $o.FreePhysicalMemory }
}
Sec 'board' {
  $b = Get-CimInstance Win32_BaseBoard
  $bios = Get-CimInstance Win32_BIOS
  $cs = Get-CimInstance Win32_ComputerSystem
  $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
  [pscustomobject]@{ maker = $cs.Manufacturer; model = $cs.Model; board = $b.Product; biosVersion = $bios.SMBIOSBIOSVersion
    biosDate = (D $bios.ReleaseDate); cpu = ("" + $cpu.Name).Trim(); cores = $cpu.NumberOfCores; threads = $cpu.NumberOfLogicalProcessors }
}
SecL 'ram' {
  Get-CimInstance Win32_PhysicalMemory | ForEach-Object {
    [pscustomobject]@{ slot = $_.DeviceLocator; gb = [math]::Round($_.Capacity / 1GB, 1); rated = $_.Speed
      configured = $_.ConfiguredClockSpeed; maker = ("" + $_.Manufacturer).Trim(); part = ("" + $_.PartNumber).Trim()
      type = $_.SMBIOSMemoryType; volt = $_.ConfiguredVoltage }
  }
}
SecL 'disks' {
  Get-PhysicalDisk | ForEach-Object {
    $d = $_
    $c = $d | Get-StorageReliabilityCounter
    [pscustomobject]@{ name = $d.FriendlyName; media = "$($d.MediaType)"; bus = "$($d.BusType)"; health = "$($d.HealthStatus)"
      op = "$($d.OperationalStatus)"; gb = [math]::Round($d.Size / 1GB); temp = $c.Temperature; wear = $c.Wear
      readErr = $c.ReadErrorsTotal; writeErr = $c.WriteErrorsTotal; hours = $c.PowerOnHours }
  }
}
SecL 'volumes' {
  Get-Volume | Where-Object { $_.DriveLetter } | ForEach-Object {
    [pscustomobject]@{ letter = "$($_.DriveLetter)"; label = $_.FileSystemLabel; fs = $_.FileSystem
      gb = [math]::Round($_.Size / 1GB, 1); freeGb = [math]::Round($_.SizeRemaining / 1GB, 1) }
  }
}
SecL 'gpus' {
  Get-CimInstance Win32_VideoController | ForEach-Object {
    [pscustomobject]@{ name = $_.Name; driver = $_.DriverVersion; date = (D $_.DriverDate) }
  }
}
SecL 'pnp' {
  Get-PnpDevice -PresentOnly | Where-Object { $_.Status -ne 'OK' } | ForEach-Object {
    [pscustomobject]@{ cls = $_.Class; name = $_.FriendlyName; status = "$($_.Status)"; problem = "$($_.Problem)" }
  }
}
Sec 'power' {
  $p = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power'
  $pend = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending')
  [pscustomobject]@{ fastStartup = $p.HiberbootEnabled; pendingReboot = [bool]$pend; plan = ((powercfg /getactivescheme | Out-String).Trim()) }
}
SecL 'hotfix' {
  Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 8 | ForEach-Object {
    [pscustomobject]@{ id = $_.HotFixID; on = (D $_.InstalledOn) }
  }
}
SecL 'minidumps' {
  Get-ChildItem 'C:\Windows\Minidump' | ForEach-Object { [pscustomobject]@{ name = $_.Name; t = (D $_.LastWriteTime); kb = [math]::Round($_.Length / 1KB) } }
}
SecL 'ev_power'  { Ev @{LogName='System'; ProviderName='Microsoft-Windows-Kernel-Power'; Id=41; StartTime=$start} 500 }
SecL 'ev_bugcheck' { Ev @{LogName='System'; ProviderName='Microsoft-Windows-WER-SystemErrorReporting'; Id=1001; StartTime=$start} 500 }
SecL 'ev_whea' { Ev @{LogName='System'; ProviderName='Microsoft-Windows-WHEA-Logger'; StartTime=$start} 200 }
SecL 'ev_disk' { Ev @{LogName='System'; ProviderName='disk','Ntfs','stornvme','storahci','storport'; Level=1,2,3; StartTime=$start} 200 }
SecL 'ev_dump' { Ev @{LogName='System'; ProviderName='volmgr'; Id=161; StartTime=$start} 200 }
SecL 'ev_memdiag' { Ev @{LogName='System'; ProviderName='Microsoft-Windows-MemoryDiagnostics-Results','Microsoft-Windows-Memory-Diagnostic-Task-Handler'; StartTime=$start90} 50 }
SecL 'ev_gpu' { Ev @{LogName='System'; ProviderName='Display','nvlddmkm','amdkmdag','amdwddmg'; StartTime=$start} 100 }
SecL 'ev_exhaust' { Ev @{LogName='System'; ProviderName='Microsoft-Windows-Resource-Exhaustion-Detector'; Id=2004; StartTime=$start} 50 }
SecL 'ev_apps' {
  Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Application Error'; Id=1000; StartTime=$start} -MaxEvents 600 -ErrorAction SilentlyContinue | ForEach-Object {
    $p = $_.Properties
    [pscustomobject]@{ t = (D $_.TimeCreated); app = "$($p[0].Value)"; module = "$($p[3].Value)"; code = "$($p[6].Value)" }
  }
}
SecL 'ev_hangs' {
  Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Application Hang'; Id=1002; StartTime=$start} -MaxEvents 300 -ErrorAction SilentlyContinue | ForEach-Object {
    [pscustomobject]@{ t = (D $_.TimeCreated); app = "$($_.Properties[0].Value)" }
  }
}
SecL 'ev_update' { Ev @{LogName='System'; ProviderName='Microsoft-Windows-WindowsUpdateClient'; Level=2; StartTime=$start} 50 }
SecL 'startup' {
  Get-CimInstance Win32_StartupCommand | ForEach-Object {
    $c = "" + $_.Command
    if ($c.Length -gt 120) { $c = $c.Substring(0, 120) }
    [pscustomobject]@{ name = $_.Name; loc = $_.Location; cmd = $c }
  }
}
Sec 'perf' {
  $before = @{}
  Get-Process | ForEach-Object { if ($_.CPU) { $before[$_.Id] = $_.CPU } }
  $cpu = @(); $dq = @(); $dl = @(); $db = @(); $cm = @()
  for ($i = 0; $i -lt 3; $i++) {
    Start-Sleep -Seconds 1
    $p = Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'"
    $d = Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk -Filter "Name='_Total'"
    $m = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
    $cpu += [double]$p.PercentProcessorTime; $dq += [double]$d.AvgDiskQueueLength
    $dl += [double]$d.AvgDisksecPerTransfer; $db += [double]$d.PercentDiskTime; $cm += [double]$m.PercentCommittedBytesInUse
  }
  $n = [Environment]::ProcessorCount
  $procs = Get-Process | ForEach-Object {
    $c0 = $before[$_.Id]
    $pc = 0
    if ($c0 -ne $null -and $_.CPU) { $pc = [math]::Round((($_.CPU - $c0) / 3) / $n * 100, 1) }
    [pscustomobject]@{ name = $_.ProcessName; cpu = $pc; ramMb = [math]::Round($_.WorkingSet64 / 1MB) }
  }
  $by = $procs | Group-Object name | ForEach-Object {
    [pscustomobject]@{ name = $_.Name; cpu = [math]::Round(($_.Group | Measure-Object cpu -Sum).Sum, 1); ramMb = ($_.Group | Measure-Object ramMb -Sum).Sum; n = $_.Count }
  }
  [pscustomobject]@{
    cpuAvg = [math]::Round(($cpu | Measure-Object -Average).Average, 1)
    diskQueue = [math]::Round(($dq | Measure-Object -Average).Average, 2)
    diskLatMs = [math]::Round((($dl | Measure-Object -Average).Average) * 1000, 1)
    diskBusy = [math]::Round(($db | Measure-Object -Average).Average, 1)
    commit = [math]::Round(($cm | Measure-Object -Average).Average, 1)
    topCpu = @($by | Sort-Object cpu -Descending | Select-Object -First 6)
    topRam = @($by | Sort-Object ramMb -Descending | Select-Object -First 6)
  }
}
function PingTest($addr) {
  if (-not $addr) { return $null }
  $p = New-Object System.Net.NetworkInformation.Ping
  $ok = 0; $times = @()
  for ($i = 0; $i -lt 5; $i++) {
    try { $x = $p.Send($addr, 1500); if ($x.Status -eq 'Success') { $ok++; $times += [double]$x.RoundtripTime } } catch { }
  }
  $avg = $null; $max = $null
  if ($times.Count -gt 0) { $avg = [math]::Round(($times | Measure-Object -Average).Average, 1); $max = ($times | Measure-Object -Maximum).Maximum }
  [pscustomobject]@{ addr = $addr; sent = 5; ok = $ok; avg = $avg; max = $max }
}
Sec 'net' {
  $ad = @(Get-NetAdapter -Physical | ForEach-Object {
    [pscustomobject]@{ name = $_.Name; desc = $_.InterfaceDescription; status = "$($_.Status)"; speed = "$($_.LinkSpeed)"; media = "$($_.PhysicalMediaType)" }
  })
  $cfg = @(Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway })
  $gw = $null; $dns = @()
  if ($cfg.Count -gt 0) { $gw = "$($cfg[0].IPv4DefaultGateway.NextHop)"; $dns = @($cfg[0].DNSServer.ServerAddresses | ForEach-Object { "$_" }) }
  $sig = $null
  $w = (netsh wlan show interfaces | Out-String)
  if ($w -match '(?m)^\s*(Signal|Sinal)\s*:\s*(\d+)%') { $sig = [int]$Matches[2] }
  $t0 = Get-Date; $dnsOk = $false
  try { $null = Resolve-DnsName 'www.microsoft.com' -Type A -ErrorAction Stop; $dnsOk = $true } catch { }
  $dnsMs = [math]::Round(((Get-Date) - $t0).TotalMilliseconds)
  [pscustomobject]@{ adapters = $ad; gateway = $gw; dns = $dns; wifiSignal = $sig; dnsOk = $dnsOk; dnsMs = $dnsMs
    pingGw = (PingTest $gw); pingNet1 = (PingTest '1.1.1.1'); pingNet2 = (PingTest '8.8.8.8') }
}
Sec 'update' {
  $s = Get-Service wuauserv
  [pscustomobject]@{ wuStatus = "$($s.Status)"; wuStart = "$($s.StartType)" }
}
Sec 'security' {
  $m = Get-MpComputerStatus
  $fw = @(Get-NetFirewallProfile | ForEach-Object { [pscustomobject]@{ name = "$($_.Name)"; on = "$($_.Enabled)" } })
  [pscustomobject]@{ av = "$($m.AntivirusEnabled)"; rtp = "$($m.RealTimeProtectionEnabled)"; sigAge = $m.AntivirusSignatureAge; quickAge = $m.QuickScanAge; fw = $fw }
}
SecL 'threats' {
  Get-MpThreatDetection | Where-Object { $_.InitialDetectionTime -gt $start } | Select-Object -First 10 | ForEach-Object {
    [pscustomobject]@{ t = (D $_.InitialDetectionTime); id = "$($_.ThreatID)" }
  }
}
$r['errors'] = @($script:errs)
$r['collected'] = (D (Get-Date))
$r | ConvertTo-Json -Depth 6 -Compress
""";
}
