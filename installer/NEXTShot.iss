; NEXT-Shot — Windows installer (Inno Setup 6)

#ifndef MyAppVersion
  #define MyAppVersion "1.0.27"
#endif

#define MyAppName "NEXT-Shot"
#define MyAppPublisher "NEXTShot"
#define MyAppExeName "NEXTShot.exe"
#define StagingDir "staging"
#define OutputDir "output"

[Setup]
AppId={{A8F3D2E1-9C4B-4A5E-B6D7-E8F901234567}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\NEXTShot
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir={#OutputDir}
OutputBaseFilename=NEXTShot-Setup-{#MyAppVersion}
SetupIconFile=..\NEXTShot\Resources\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#StagingDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
