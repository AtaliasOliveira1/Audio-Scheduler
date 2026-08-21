; Script do Inno Setup para o AudioScheduler
#define MyAppName "AudioScheduler"
#define MyAppVersion "1.0.5"
#define MyAppPublisher "Atalias Lô-Amí"
#define MyAppExeName "AudioScheduler_v1.0.5.exe"
#define MyAppIcon "F:\DESIGNER (não apagar)\BACKUP DOCUMENTOS\AudioSchedulerCSharp - Mixer\logo.ico"

[Setup]
; Identificador único do programa
AppId={{C8E39D21-12A4-4B7C-8D92-A92B1C3D4E5F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; Nome do arquivo do instalador que vai ser criado
OutputBaseFilename=AudioScheduler_v1.0.5_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern

; Define o ícone do arquivo do instalador (.exe do Setup)
SetupIconFile={#MyAppIcon}

; Define o ícone que aparece no "Adicionar ou Remover Programas" do Windows
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Copia o arquivo de ícone para dentro da pasta instalada
Source: "{#MyAppIcon}"; DestDir: "{app}"; Flags: ignoreversion

; Copia o executável principal
Source: "F:\DESIGNER (não apagar)\BACKUP DOCUMENTOS\AudioSchedulerCSharp - Mixer\publish-v1.0.5-beta\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Copia TODOS os arquivos e subpastas da pasta publish
Source: "F:\DESIGNER (não apagar)\BACKUP DOCUMENTOS\AudioSchedulerCSharp - Mixer\publish-v1.0.5-beta\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Cria o atalho no Menu Iniciar com o ícone personalizado
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\logo.ico"

; Cria o atalho na Área de Trabalho com o ícone personalizado
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\logo.ico"; Tasks: desktopicon

[Run]
; Opção para rodar o programa logo após terminar a instalação (Corrigido)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: postinstall skipifsilent