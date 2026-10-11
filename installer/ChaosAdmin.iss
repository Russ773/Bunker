; Build with: iscc /DAppVersion=0.0.1 /DSourceDir=..\publish installer\ChaosAdmin.iss
#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{B6F3C7E2-4A51-4C8E-9D2B-5E1A7C3F0A11}
AppName=Chaos Admin
AppVersion={#AppVersion}
AppPublisher=Chaos Admin
DefaultDirName={autopf}\Chaos Admin
DefaultGroupName=Chaos Admin
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=ChaosAdmin-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\ChaosAdmin.exe
CloseApplications=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Chaos Admin"; Filename: "{app}\ChaosAdmin.exe"
Name: "{autodesktop}\Chaos Admin"; Filename: "{app}\ChaosAdmin.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Run]
; No skipifsilent: silent auto-updates relaunch the app when done.
Filename: "{app}\ChaosAdmin.exe"; Description: "Launch Chaos Admin"; Flags: nowait postinstall
