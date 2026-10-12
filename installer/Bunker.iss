; Build with: iscc /DAppVersion=1.0.9 /DSourceDir=..\publish installer\Bunker.iss
#ifndef AppVersion
  #define AppVersion "1.0.9"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
; Keep this ID the same forever: it is how updates replace the existing install.
AppId={{B6F3C7E2-4A51-4C8E-9D2B-5E1A7C3F0A11}
AppName=Bunker
AppVersion={#AppVersion}
AppVerName=Bunker {#AppVersion}
AppPublisher=Bunker
AppPublisherURL=https://github.com/Russ773/Bunker
AppSupportURL=https://github.com/Russ773/Bunker/issues
AppUpdatesURL=https://github.com/Russ773/Bunker/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Bunker
DefaultGroupName=Bunker
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Bunker-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
UninstallDisplayName=Bunker
UninstallDisplayIcon={app}\Bunker.exe

; Look and feel
WizardStyle=modern
SetupIconFile=..\src\Bunker.ico
WizardImageFile=images\wizard-164.bmp,images\wizard-328.bmp
WizardSmallImageFile=images\small-55.bmp,images\small-110.bmp
WizardImageBackColor=$171311
WizardImageAlphaFormat=defined

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
BeveledLabel=Bunker {#AppVersion}
WelcomeLabel1=Welcome to Bunker
WelcomeLabel2=Bunker lets you watch your Rust server's console and manage it from your desktop, without having to be in the game.%n%nThis will install Bunker {#AppVersion} on your computer. It's a good idea to close any running copy of Bunker before you continue.
FinishedHeadingLabel=Bunker is ready
FinishedLabelNoIcons=Bunker has been installed.
FinishedLabel=Bunker has been installed. You can start it from the Start menu or a shortcut.

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Bunker"; Filename: "{app}\Bunker.exe"
Name: "{group}\Uninstall Bunker"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Bunker"; Filename: "{app}\Bunker.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
; No skipifsilent: after an update (silent install) the app starts again by itself.
Filename: "{app}\Bunker.exe"; Description: "Launch Bunker"; Flags: nowait postinstall
