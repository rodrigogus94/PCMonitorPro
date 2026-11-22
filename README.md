# PC Monitor Pro

Monitor, diagnóstico e ferramentas de desempenho para **Windows 10/11**, em um único programa (C# / WPF / .NET 8).
Junta o **RESET EXPLODE** (MIT, de GustavoReset) com o **PC Monitor** (widgets, histórico de quedas e diagnóstico do Windows),
com correções de segurança e de bugs. Veja [NOTICE.md](NOTICE.md) para a origem e as licenças.

> **Estado atual: fase 1 concluída** — base do RESET EXPLODE renomeada + correções de segurança/bugs + testes + CI.
> **Fase 2 concluída:** histórico, diagnóstico, marco, comandos, alertas, iniciar com o Windows e widgets flutuantes já estão no programa. Resta a fase 3 (integração da navegação, marca própria, docs).

## O que já existe (vindo do RESET EXPLODE)
- **Sistema**: SO, placa-mãe/BIOS, CPU, RAM por módulo, GPU (estilo GPU-Z), discos e S.M.A.R.T., energia/bateria, rede (latência, perda, bufferbloat), relatório HTML/JSON.
- **Sensores em tempo real** (LibreHardwareMonitor) e visão geral com gráficos.
- **Jogos**: FPS estimado, detector de gargalo, overlay (FPS por ETW) e lançador com perfil por jogo.
- **Desempenho**: benchmarks (CPU, GPU D3D11, ray tracing DXR, simulação, memória, disco), stress test de CPU/RAM/GPU e verificação de overclock.
- **Ferramentas**: otimizador, inicialização do Windows, instalar apps (winget), drivers (NVIDIA/AMD) e integração de iluminação (Corsair, OpenRGB, Razer).
- **RESET MAKER**: portas COM e drivers seriais (CH340, CP210x, FTDI, Prolific, Arduino) incluídos no programa.

## A portar do PC Monitor (fase 2)
- [x] Widgets flutuantes configuráveis (página **Widgets**: várias janelas, cantos magnéticos, cores, opacidade, tamanho, clique atravessa, escolha e ordem dos sensores)
- [x] Histórico em arquivo com detecção de queda (sessão sem marca de fim normal) e resumo dos últimos valores
- [x] Alertas de temperatura e RAM (aviso no canto da tela, com cooldown por sensor; configurável na página Diagnóstico)
- [x] Diagnóstico do Windows por cenário (tela azul, lentidão, calor, disco, rede, vídeo, update), com causa mais provável, evidências e passos
- [x] Marco de comparação (antes/depois de uma mudança) e relatório automático em `Documentos\PCMonitor`
- [x] Comandos sugeridos de PowerShell (copiar ou executar com confirmação)
- [x] Iniciar com o Windows (Agendador de Tarefas elevado; só ativa se a pasta do programa estiver protegida contra alterações por programas comuns)
- [x] Botão "Abrir log" (página Diagnóstico)

## Correções de segurança e bugs (fase 1)
| Problema no original | Correção |
|---|---|
| DLL da Corsair aceita sem assinatura e carregada de `%AppData%` (DLL planting em processo elevado) | Só carrega DLL com assinatura válida da Corsair, da pasta do programa ou de `%ProgramData%\PCMonitorPro` (só administrador), travada contra troca; fonte e pacotes que não sejam `iCUESDK_*.zip` são recusados |
| Driver/instalador verificado e executado em `%TEMP%` (troca entre conferir e executar) | Pasta `%ProgramData%\PCMonitorPro` só de administrador (`SecureDir`), com checagem de dono e permissões; falha fechada se a pasta não for segura |
| Assinante conferido por "contém NVIDIA" e URL sem checagem de domínio | Campo **O** do certificado comparado de forma exata; só `https` e domínios do fabricante |
| Jogos e programas auxiliares iniciados como administrador (e dirigidos por `settings.json` gravável) | Sempre iniciados **sem elevação** (token do Explorer); URIs também; só dígitos no ID da Steam |
| `settings.json` corrompido apagava os backups de "Restaurar originais"; gravação não atômica | Carga segura (cópia `.corrompido-…` + último `.bak`), gravação atômica, e arquivo preso por outro programa não é sobrescrito |
| Backup do `localconfig.vdf` da Steam sobrescrito a cada alteração | O `.pcmonitorpro.bak` original nunca é sobrescrito; `.prev` guarda o estado anterior |
| 98 `catch { }` vazios e nenhum log | `AppLog` (arquivo com rotação e limitação de repetições) + handlers globais para exceções não tratadas (inclui `async void`) |
| Release a cada push sem checksum e com actions por tag | CI de build/testes; release **só por tag**, com `SHA256SUMS.txt`, atestado de procedência e actions fixadas por SHA |

Riscos que continuam: o programa é **unsigned** e pede administrador (necessário para os sensores), então o Windows/antivírus pode
avisar. Baixe apenas das releases do repositório e confira o `SHA256SUMS.txt`.

## Compilar e testar
Requer o .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`).
```
dotnet build PCMonitorPro.sln -c Release
dotnet test  PCMonitorPro.sln -c Release
dotnet run --project src/PCMonitorPro        # pede administrador
```
Para temperatura/consumo da CPU, instale o driver PawnIO uma vez: `winget install namazso.PawnIO`.

Dados e log ficam em `%AppData%\PCMonitorPro` (`settings.json`, `pcmonitor.log`).

## Licença
MIT (veja [LICENSE](LICENSE)); os drivers embutidos e as bibliotecas têm licenças próprias ([NOTICE.md](NOTICE.md)).
