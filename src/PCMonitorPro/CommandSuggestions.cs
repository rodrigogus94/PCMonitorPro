using System.Text;

namespace PCMonitorPro;

/// <summary>Comando de PowerShell sugerido. Kind: "leitura" (só consulta) ou "altera" (muda algo ou inicia reparo/verificação).</summary>
public sealed class SuggestedCommand
{
    public string Title { get; }
    public string Desc { get; }
    public string Command { get; }
    public string Kind { get; }
    public bool Admin { get; }
    public HashSet<string> Tags { get; }

    public SuggestedCommand(string title, string desc, string command, string kind, bool admin, string tags)
    {
        Title = title; Desc = desc; Command = command; Kind = kind; Admin = admin;
        Tags = new HashSet<string>(tags.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public bool Alters => Kind != "leitura";
    public string Flags => (Alters ? "ALTERA o sistema" : "só consulta") + (Admin ? ", exige administrador" : "");
}

public sealed record CommandSuggestion(SuggestedCommand Command, string Group);

/// <summary>Comandos de PowerShell sugeridos conforme o problema. NUNCA são executados automaticamente: o usuário lê, copia ou executa um a um.</summary>
public static class CommandSuggestions
{
    public const string GroupLinked = "Ligados às causas prováveis";
    public const string GroupFocus = "Do foco escolhido";
    public const string GroupAll = "Todos os comandos";

    public const string Warning = "Nenhum comando é executado automaticamente. Você lê, pode copiar para o seu próprio PowerShell ou " +
                                  "escolher executar aqui, um por vez, com confirmação.";

    public static readonly IReadOnlyList<SuggestedCommand> All = new List<SuggestedCommand>
    {
        new("Resumo do sistema", "Versão do Windows, build, BIOS e memória total.",
            "Get-ComputerInfo | Select-Object OsName,OsVersion,OsBuildNumber,BiosSMBIOSBIOSVersion,CsTotalPhysicalMemory | Format-List",
            "leitura", false, "crash os perf update"),
        new("Quando o PC foi ligado pela última vez", "Mostra o último boot e há quanto tempo o PC está ligado.",
            "$b=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime; $b; 'Ligado há: ' + ((Get-Date)-$b).ToString('d\\.hh\\:mm\\:ss')",
            "leitura", false, "crash perf power"),
        new("Reinícios inesperados (Kernel-Power 41)", "Lista as últimas 20 vezes em que o Windows reiniciou sem desligar direito.",
            "Get-WinEvent -FilterHashtable @{LogName='System';Id=41} -MaxEvents 20 -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id | Format-Table -AutoSize",
            "leitura", false, "crash hw power"),
        new("Telas azuis registradas", "Mostra data e código das últimas telas azuis (BugCheck).",
            "Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-WER-SystemErrorReporting'} -MaxEvents 20 -ErrorAction SilentlyContinue | Select-Object TimeCreated,Message | Format-List",
            "leitura", false, "crash driver mem"),
        new("Erros de hardware (WHEA)", "Erros reportados pela CPU, memória ou barramento PCIe. Qualquer resultado aqui merece atenção.",
            "Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-WHEA-Logger'} -MaxEvents 20 -ErrorAction SilentlyContinue | Format-List TimeCreated,Id,Message",
            "leitura", false, "crash hw mem"),
        new("Arquivos de minidump", "Lista os dumps de memória gerados nas telas azuis (abra no BlueScreenView ou WinDbg).",
            "Get-ChildItem C:\\Windows\\Minidump -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object Name,Length,LastWriteTime | Format-Table -AutoSize",
            "leitura", false, "crash driver"),
        new("Drivers instalados mais recentemente", "Um driver novo pode ser o culpado se os problemas começaram depois dele.",
            "Get-CimInstance Win32_PnPSignedDriver | Where-Object DriverDate | Sort-Object DriverDate -Descending | Select-Object -First 20 DeviceName,DriverVersion,DriverDate,Manufacturer | Format-Table -AutoSize",
            "leitura", false, "apps crash driver video"),
        new("Ver a tela azul antes de reiniciar", "Desliga o reinício automático após falha, para você conseguir ler o código da tela azul.",
            "Set-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\CrashControl' -Name AutoReboot -Value 0",
            "altera", true, "crash"),
        new("Windows em canal de testes (Insider)?", "Mostra se o PC está em um canal Insider, que pode ter bugs.",
            "Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\WindowsSelfHost\\Applicability' -ErrorAction SilentlyContinue | Select-Object BranchName,ContentType,Ring | Format-List",
            "leitura", false, "crash os"),
        new("Pentes de memória e velocidade", "Mostra fabricante, capacidade e a velocidade anunciada x configurada (EXPO/DOCP).",
            "Get-CimInstance Win32_PhysicalMemory | Select-Object BankLabel,Manufacturer,PartNumber,@{n='GB';e={$_.Capacity/1GB}},Speed,ConfiguredClockSpeed | Format-Table -AutoSize",
            "leitura", false, "crash mem perf"),
        new("Resultado do Teste de Memória do Windows", "Mostra o resultado dos testes já realizados com o mdsched.",
            "Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-MemoryDiagnostics-Results'} -MaxEvents 10 -ErrorAction SilentlyContinue | Format-List TimeCreated,Message",
            "leitura", false, "crash mem"),
        new("Agendar o Teste de Memória do Windows", "Abre o assistente; se você escolher reiniciar, o PC reinicia para testar a RAM.",
            "mdsched.exe",
            "altera", false, "crash mem"),
        new("Maiores consumidores de memória", "Os 10 programas que mais usam RAM agora.",
            "Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 10 Name,Id,@{n='RAM(MB)';e={[int]($_.WorkingSet64/1MB)}} | Format-Table -AutoSize",
            "leitura", false, "apps mem perf"),
        new("Programas que travaram ou fecharam (evento 1000)", "Mostra o programa, o módulo e o código de erro das últimas falhas.",
            "Get-WinEvent -FilterHashtable @{LogName='Application';Id=1000} -MaxEvents 20 -ErrorAction SilentlyContinue | Select-Object TimeCreated,@{n='Programa';e={$_.Properties[0].Value}},@{n='Modulo';e={$_.Properties[3].Value}},@{n='Codigo';e={$_.Properties[6].Value}} | Format-Table -AutoSize",
            "leitura", false, "apps crash mem"),
        new("Programas sem resposta (evento 1002)", "Programas que o Windows encerrou por ficarem travados.",
            "Get-WinEvent -FilterHashtable @{LogName='Application';Id=1002} -MaxEvents 20 -ErrorAction SilentlyContinue | Select-Object TimeCreated,@{n='Programa';e={$_.Properties[0].Value}} | Format-Table -AutoSize",
            "leitura", false, "apps perf"),
        new("Maiores consumidores de CPU", "Os 10 processos com mais tempo de CPU.",
            "Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 Name,Id,@{n='CPU(s)';e={[int]$_.CPU}},@{n='RAM(MB)';e={[int]($_.WorkingSet64/1MB)}} | Format-Table -AutoSize",
            "leitura", false, "perf thermal"),
        new("Uso de CPU agora", "Porcentagem de uso do processador neste instante.",
            "(Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter \"Name='_Total'\").PercentProcessorTime",
            "leitura", false, "perf thermal"),
        new("Plano de energia ativo", "Plano 'Economia' pode deixar o PC lento; 'Equilibrado' ou 'Alto desempenho' costumam ser melhores.",
            "powercfg /getactivescheme; powercfg /list",
            "leitura", false, "perf power thermal"),
        new("Programas que iniciam com o Windows", "Quanto mais itens, mais demorado o boot e mais RAM ocupada.",
            "Get-CimInstance Win32_StartupCommand | Select-Object Name,Command,Location | Format-Table -AutoSize -Wrap",
            "leitura", false, "perf startup"),
        new("Limpar arquivos temporários do usuário", "Apaga o conteúdo da pasta TEMP do seu usuário (arquivos em uso são ignorados).",
            "Get-ChildItem $env:TEMP -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue",
            "altera", false, "disk perf"),
        new("Saúde dos discos", "Estado de saúde informado pelo Windows para cada disco físico.",
            "Get-PhysicalDisk | Select-Object FriendlyName,MediaType,HealthStatus,OperationalStatus,@{n='GB';e={[int]($_.Size/1GB)}} | Format-Table -AutoSize",
            "leitura", false, "crash disk perf"),
        new("Desgaste, temperatura e erros do disco (SMART)", "Horas ligado, desgaste do SSD, temperatura e erros de leitura/escrita.",
            "Get-PhysicalDisk | Get-StorageReliabilityCounter | Select-Object DeviceId,Temperature,Wear,ReadErrorsTotal,WriteErrorsTotal,PowerOnHours | Format-Table -AutoSize",
            "leitura", true, "crash disk"),
        new("Espaço livre nos volumes", "Menos de 15% livre deixa o Windows lento.",
            "Get-Volume | Where-Object DriveLetter | Select-Object DriveLetter,FileSystemLabel,@{n='Livre(GB)';e={[int]($_.SizeRemaining/1GB)}},@{n='Total(GB)';e={[int]($_.Size/1GB)}},HealthStatus | Format-Table -AutoSize",
            "leitura", false, "disk perf update"),
        new("Erros de disco no registro de eventos", "Eventos 'disk' e 'Ntfs' recentes (setor ruim, falha de leitura, reset).",
            "Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='disk','Ntfs'} -MaxEvents 20 -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,ProviderName,Message | Format-List",
            "leitura", false, "crash disk"),
        new("Verificar o disco C: (somente leitura)", "CHKDSK online, sem corrigir nada. Pode levar vários minutos.",
            "chkdsk C: /scan",
            "leitura", true, "crash disk"),
        new("Adaptadores de rede", "Estado e velocidade de cada placa de rede (Wi-Fi/cabo).",
            "Get-NetAdapter | Select-Object Name,InterfaceDescription,Status,LinkSpeed | Format-Table -AutoSize",
            "leitura", false, "net"),
        new("Configuração de IP, gateway e DNS", "Mostra o IP, o roteador (gateway) e os servidores DNS em uso.",
            "Get-NetIPConfiguration",
            "leitura", false, "net"),
        new("Teste: roteador responde?", "Se o roteador responde mas a internet não, o problema está no provedor.",
            "Test-Connection (Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Select-Object -First 1 -ExpandProperty NextHop) -Count 4",
            "leitura", false, "net"),
        new("Teste: internet por IP", "Ping em 8.8.8.8 (sem depender de DNS).",
            "Test-Connection 8.8.8.8 -Count 4",
            "leitura", false, "net"),
        new("Teste: DNS", "Se falhar enquanto o ping por IP funciona, o problema é DNS.",
            "Resolve-DnsName google.com",
            "leitura", false, "net"),
        new("Teste: conexão HTTPS", "Testa a porta 443 de um site conhecido.",
            "Test-NetConnection google.com -Port 443 | Select-Object ComputerName,RemotePort,TcpTestSucceeded,PingSucceeded | Format-List",
            "leitura", false, "net"),
        new("Limpar o cache de DNS", "Descarta nomes salvos; ajuda quando um site não abre mas outros sim.",
            "Clear-DnsClientCache",
            "altera", false, "net"),
        new("Placa de vídeo e driver", "Modelo, versão e data do driver, resolução e taxa de atualização.",
            "Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,DriverDate,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate | Format-List",
            "leitura", false, "crash gpu video"),
        new("Painel da placa NVIDIA (nvidia-smi)", "Uso, temperatura, clocks e consumo da GPU (só com driver NVIDIA).",
            "nvidia-smi",
            "leitura", false, "gpu thermal video"),
        new("Falhas do driver de vídeo (TDR)", "Eventos em que o driver de vídeo parou de responder e foi reiniciado.",
            "Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='nvlddmkm','amdkmdag','Display'} -MaxEvents 20 -ErrorAction SilentlyContinue | Format-List TimeCreated,ProviderName,Message",
            "leitura", false, "crash gpu video"),
        new("Atualizações instaladas recentemente", "Se o problema começou depois de uma atualização, ela é suspeita.",
            "Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 10 HotFixID,Description,InstalledOn | Format-Table -AutoSize",
            "leitura", false, "crash os update"),
        new("Serviços do Windows Update", "Os serviços devem existir e não estar desativados.",
            "Get-Service wuauserv,bits,cryptsvc,TrustedInstaller | Select-Object Name,Status,StartType | Format-Table -AutoSize",
            "leitura", false, "update"),
        new("Falhas recentes do Windows Update", "Erros registrados pelo cliente de atualização.",
            "Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-WindowsUpdateClient';Level=2} -MaxEvents 15 -ErrorAction SilentlyContinue | Format-List TimeCreated,Id,Message",
            "leitura", false, "update"),
        new("Verificar integridade (DISM CheckHealth)", "Checagem rápida: só informa se o repositório do Windows está corrompido.",
            "DISM /Online /Cleanup-Image /CheckHealth",
            "leitura", true, "disk os update"),
        new("Reparar a imagem do Windows (DISM RestoreHealth)", "Baixa e corrige arquivos do sistema. Pode levar de 10 a 30 minutos.",
            "DISM /Online /Cleanup-Image /RestoreHealth",
            "altera", true, "disk os update"),
        new("Reparar arquivos do sistema (SFC)", "Verifica e corrige arquivos protegidos do Windows. Pode levar de 5 a 30 minutos.",
            "sfc /scannow",
            "altera", true, "apps crash os update"),
        new("Reiniciar o serviço do Windows Update", "Ajuda quando a atualização trava em 'verificando'.",
            "Restart-Service wuauserv",
            "altera", true, "update"),
        new("Estado do antivírus do Windows", "Proteção em tempo real e data das definições de vírus.",
            "Get-MpComputerStatus | Select-Object AntivirusEnabled,RealTimeProtectionEnabled,AntispywareEnabled,AntivirusSignatureLastUpdated | Format-List",
            "leitura", false, "perf security"),
        new("Ameaças detectadas pelo Defender", "Histórico de detecções.",
            "Get-MpThreatDetection | Select-Object InitialDetectionTime,ThreatID,Resources | Format-List",
            "leitura", false, "perf security"),
        new("Atualizar definições de vírus", "Baixa as definições mais recentes do Defender.",
            "Update-MpSignature",
            "altera", false, "security"),
        new("Verificação rápida de vírus", "Inicia uma verificação rápida do Defender (usa CPU e disco enquanto roda).",
            "Start-MpScan -ScanType QuickScan",
            "altera", false, "perf security"),
    };

    /// <summary>Lista (comando, grupo). focus = null → todos do cenário. hypTags: etiquetas das hipóteses mais prováveis.</summary>
    public static List<CommandSuggestion> Suggest(HashSet<string>? focus, HashSet<string>? hypTags = null, bool showAll = false)
    {
        var res = new List<CommandSuggestion>();
        foreach (var c in All)
        {
            string group;
            if (showAll) group = GroupAll;
            else if (hypTags is { Count: > 0 } && c.Tags.Overlaps(hypTags)) group = GroupLinked;
            else if (focus == null || c.Tags.Overlaps(focus)) group = GroupFocus;
            else continue;
            res.Add(new(c, group));
        }
        int Order(string g) => g == GroupLinked ? 0 : g == GroupFocus ? 1 : 2;
        return res.OrderBy(x => Order(x.Group)).ToList();   // OrderBy é estável: mantém a ordem original dentro do grupo
    }

    public static string ToText(IEnumerable<CommandSuggestion> items)
    {
        var L = new List<string> { "COMANDOS SUGERIDOS DO POWERSHELL (não são executados automaticamente; você escolhe se e quando rodar)" };
        foreach (var s in items)
        {
            L.Add($"  # {s.Command.Title} [{s.Command.Flags}]");
            L.Add($"  {s.Command.Command}");
            L.Add("");
        }
        return string.Join("\n", L);
    }

    /// <summary>Comandos para anexar ao relatório: os ligados às 2 hipóteses principais (até 12) ou, sem eles, os 8 primeiros do foco.</summary>
    public static string ForReport(DiagnosticReport rep)
    {
        var focus = Diagnostics.Scenarios.TryGetValue(rep.Scenario, out var s) ? s.Focus : null;
        var hyp = new HashSet<string>();
        foreach (var h in rep.Hypotheses.Take(2).Where(h => h.Score > 0)) hyp.UnionWith(Diagnostics.HypTags[h.Key]);
        var items = Suggest(focus, hyp.Count > 0 ? hyp : null).Where(x => x.Group == GroupLinked).ToList();
        if (items.Count == 0) items = Suggest(focus).Take(8).ToList();
        return ToText(items.Take(12));
    }
}
