# Avisos de terceiros

O PC Monitor Pro reúne código e componentes de outros projetos. Mantenha estes avisos ao redistribuir.

## Código-base
- **RESET EXPLODE** (c) 2026 Gustavo (GustavoReset), licença MIT. Origem do código C#/WPF (sensores, GPU, benchmarks, overlay,
  otimização, inicialização, drivers, RESET MAKER, integração RGB e jogos). A licença original está em `docs/upstream/ResetExplode-LICENSE`.
  O projeto de origem não aceita pull requests; este é um derivado independente (fork) permitido pela licença.
- **PC Monitor** (c) 2026 Rodrigo Santana. Widgets, histórico de quedas, diagnóstico e comandos sugeridos (em portagem, fase 2).

## Bibliotecas (NuGet)
| Componente | Licença |
|---|---|
| LibreHardwareMonitorLib | MPL 2.0 (arquivos da biblioteca; mantenha o aviso de licença ao distribuir) |
| Microsoft.Diagnostics.Tracing.TraceEvent | MIT |
| Vortice.Direct3D11 / Direct3D12 / D3DCompiler / Dxc | MIT |
| System.Management, System.Security.Cryptography.ProtectedData | MIT (Microsoft) |
| xUnit (só nos testes) | Apache 2.0 |

## Drivers seriais embutidos (`src/PCMonitorPro/Drivers`)
**Não fazem parte da licença MIT.** São pacotes oficiais, sem modificação, com a assinatura original do fabricante
(WCH CH340/CH341, Silicon Labs CP210x, FTDI VCP, Arduino). Origem, versão e SHA-256 em `src/PCMonitorPro/Drivers/LEIA-ME.md`.
Confira os termos de redistribuição de cada fabricante antes de redistribuir o `.exe`.

## Marca e imagens
Os arquivos de `src/PCMonitorPro/Assets` (logos e ícone) vieram do RESET EXPLODE e pertencem ao autor original.
Troque-os por imagens próprias antes de publicar uma versão com a sua marca.

## Componentes externos usados em tempo de execução
- **PawnIO** (driver para ler sensores da CPU): instalado à parte pelo usuário (`winget install namazso.PawnIO`).
- **iCUE SDK da Corsair**: baixado sob demanda do repositório oficial da Corsair; só é carregado com assinatura digital válida da Corsair.
