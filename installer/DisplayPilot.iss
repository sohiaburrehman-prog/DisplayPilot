; DisplayPilot installer — per-user install to LocalAppData (no admin required).
; Build: ISCC DisplayPilot.iss  (from the installer\ folder)

#define MyAppName "DisplayPilot"
#define MyAppVersion "1.5.3"
#define MyAppPublisher "Sohiab Rehman"
#define MyAppSupportEmail "sohiab.rehman@pm.me"
#define MyAppExeName "DisplayPilot.exe"
#define PublishDir "..\publish"

; ─────────────────────────────────────────────────────────────────────────
; Optional code signing (disabled by default — DO NOT block builds on this).
; To sign, define a SignTool named "displaypilot" and build with the flag set:
;
;   ISCC.exe /DSign ^
;     "/Sdisplaypilot=\"C:\Path\signtool.exe\" sign /fd SHA256 ^
;       /f \"C:\Path\cert.pfx\" /p <password> ^
;       /tr http://timestamp.digicert.com /td SHA256 $f" ^
;     installer\DisplayPilot.iss
;
; When /DSign is passed, the installer and uninstaller are signed below.
; See installer\README.md for the full SmartScreen / signing notes.
; ─────────────────────────────────────────────────────────────────────────
#ifdef Sign
  #define SignToolName "displaypilot"
#endif

[Setup]
AppId={{8F4E2A91-3C7B-4D5E-9F12-A1B2C3D4E5F6}
#ifdef Sign
SignTool={#SignToolName}
SignedUninstaller=yes
#endif
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppSupportURL=mailto:{#MyAppSupportEmail}
DefaultDirName={localappdata}\DisplayPilot
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=output
OutputBaseFilename=DisplayPilot-Setup
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\docs\legal\EULA.txt
InfoBeforeFile=..\docs\legal\PrivacyPolicy.txt
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
CloseApplications=force
CloseApplicationsFilter=DisplayPilot.exe
; Overwrite existing install in the same directory
UsePreviousAppDir=yes
DirExistsWarning=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start DisplayPilot when &Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DisplayPilot"; ValueData: """{app}\{#MyAppExeName}"" --autostart"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
