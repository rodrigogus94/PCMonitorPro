; Instalador do PC Monitor Pro (Inno Setup 6).
; Compilar:  ISCC.exe /DAppVersion=0.17.0 /DSourceExe=..\dist\PCMonitorPro.exe installer\PCMonitorPro.iss
; Instala em Arquivos de Programas (pasta só de administrador). É isso que permite ativar "Iniciar com o Windows": o programa
; recusa essa opção se o .exe ou a pasta puderem ser alterados por programas comuns.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\dist\PCMonitorPro.exe"
#endif
#define AppName "PC Monitor Pro"
#define AppExe "PCMonitorPro.exe"
#define AppUrl "https://github.com/rodrigogus94/PCMonitorPro"

[Setup]
AppId={{8D48554A-49D0-4627-8244-095C7B3B100E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\src\PCMonitorPro\Assets\app.ico
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=PCMonitorPro-Setup-{#AppVersion}
; o .exe já vem comprimido: compressão máxima só deixaria o instalador mais lento
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; O programa precisa de administrador (sensores) e a instalação em Arquivos de Programas também.
PrivilegesRequired=admin
; Só para testes automatizados (/CURRENTUSER na linha de comando); a tela de instalação não oferece instalação por usuário.
PrivilegesRequiredOverridesAllowed=commandline
; Se o programa estiver aberto, o instalador pede para fechá-lo (e não reabre sozinho).
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Instalador do {#AppName}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "autostart"; Description: "Iniciar o {#AppName} com o Windows (como administrador; abre na bandeja se essa opção estiver ligada)"; GroupDescription: "Opções:"; Flags: unchecked; Check: IsAdminInstallMode
Name: "desktopicon"; Description: "Criar um atalho na Área de Trabalho"; GroupDescription: "Opções:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Cria a tarefa do Agendador (o programa confere se a pasta é protegida; aqui é, pois está em Arquivos de Programas).
Filename: "{app}\{#AppExe}"; Parameters: "--ativar-inicio"; Tasks: autostart; Flags: runhidden waituntilterminated; StatusMsg: "Configurando o início com o Windows..."
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Remove a tarefa de início automático (não faz nada se ela não existir).
Filename: "{app}\{#AppExe}"; Parameters: "--desativar-inicio"; Flags: runhidden waituntilterminated; RunOnceId: "RemoverInicioAutomatico"

[Code]
// Ao desinstalar, pergunta se deve apagar também os dados do programa (padrão: NÃO). Os relatórios em Documentos\PCMonitor ficam sempre.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    if MsgBox('Apagar também as configurações, o histórico de sensores e o log do PC Monitor Pro?' + #13#10 + #13#10 +
              ExpandConstant('{userappdata}\PCMonitorPro') + #13#10 +
              'Os relatórios salvos em Documentos\PCMonitor não serão apagados.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\PCMonitorPro'), True, True, True);
      DelTree(ExpandConstant('{commonappdata}\PCMonitorPro'), True, True, True);
    end;
  end;
end;
