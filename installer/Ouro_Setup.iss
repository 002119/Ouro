; Script generated for Ouro Replay Engine Installer
#define MyAppName "Ouro Replay Engine"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Skyx Gaming"
#define MyAppURL "https://github.com/002119/Ouro"
#define MyAppExeName "Ouro.exe"

[Setup]
AppId={{9F7B18C2-4E25-45C7-8A7B-0A8610EF60DE}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\Ouro
DisableProgramGroupPage=yes
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=Ouro_Setup_v1.0.0
SetupIconFile=..\Resources\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion=1.0.0.0
VersionInfoCompany=Skyx Gaming
VersionInfoDescription=Instalador oficial de Ouro Replay Engine
VersionInfoCopyright=Copyright (C) 2026 Skyx Gaming

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; El arranque con Windows se controla desde la propia app (Preferencias).
; No crear un segundo mecanismo aquí para evitar inicios duplicados.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\Ouro_Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "OuroReplayEngine"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
