; Build with: iscc /DAppVersion=0.0.1 /DSourceDir=..\publish installer\Bunker.iss
#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{B6F3C7E2-4A51-4C8E-9D2B-5E1A7C3F0A11}
AppName=Bunker
AppVersion={#AppVersion}
AppPublisher=Bunker
DefaultDirName={autopf}\Bunker
DefaultGroupName=Bunker
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Bunker-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Bunker.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Bunker.exe
CloseApplications=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Bunker"; Filename: "{app}\Bunker.exe"
Name: "{autodesktop}\Bunker"; Filename: "{app}\Bunker.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Run]
; No skipifsilent: silent auto-updates relaunch the app when done.
Filename: "{app}\Bunker.exe"; Description: "Launch Bunker"; Flags: nowait postinstall
