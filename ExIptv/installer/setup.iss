; Inno Setup Script für EX-IPTV Desktop
; Wird über GitHub Actions kompiliert. PublishDir/OutputDir kommen als /D-Defines.
; Lokaler Fallback, falls ohne Defines aufgerufen.

#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "Output"
#endif

#define MyAppName "EX-IPTV Desktop"
#define MyAppShortName "EX-IPTV"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "don1"
#define MyAppExeName "ExIptv.exe"

[Setup]
AppId={{7F3C1E90-2B4D-4A6E-9C11-EX1PTVDESK01}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppShortName}
DefaultGroupName={#MyAppShortName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=EX-IPTV-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
SetupIconFile=..\ExIptv\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; Gesamter Publish-Ordner inkl. nativer libvlc-DLLs und Plugins
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Anwendungsdaten (DB/Logs) bewusst NICHT löschen – Nutzerdaten bleiben erhalten.
Type: filesandordirs; Name: "{app}\logs"
