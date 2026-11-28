; The Windows installer, built by .github/workflows/release.yml with Inno Setup 6.3 or later:
;   ISCC /DAppVersion=1.2.3 /DSourceDir=<built app> /DOutputDir=<folder> GitWolf.iss
; It installs for the current user only, without administrator rights, so that the in-app updater can
; replace the files.

#ifndef AppVersion
  #error AppVersion is not defined
#endif
#ifndef SourceDir
  #error SourceDir is not defined
#endif
#ifndef OutputDir
  #error OutputDir is not defined
#endif

#define Program "GitWolf.App.exe"

[Setup]
; Identifies the installation to later installers. Never change it.
AppId={{6F1C3B52-9D0E-4C7A-A0B6-3E7F2D5C8A41}
AppName=GitWolf
AppVersion={#AppVersion}
AppVerName=GitWolf {#AppVersion}
AppPublisher=GitWolf
AppPublisherURL=https://github.com/ipuppyyt/GitWolf
AppSupportURL=https://github.com/ipuppyyt/GitWolf/issues
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={autopf}\GitWolf
DisableWelcomePage=no
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename=GitWolf-{#AppVersion}-windows-x64-setup
SetupIconFile=..\src\GitWolf.App\Assets\app.ico
UninstallDisplayIcon={app}\{#Program}
UninstallDisplayName=GitWolf
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Drawn from assets/gitwolf-icon.svg by tools/make-icon.ps1.
WizardImageFile=wizard-100.bmp,wizard-150.bmp,wizard-200.bmp
WizardSmallImageFile=wizard-small-100.bmp,wizard-small-150.bmp,wizard-small-200.bmp

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\GitWolf"; Filename: "{app}\{#Program}"
Name: "{autodesktop}\GitWolf"; Filename: "{app}\{#Program}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#Program}"; Description: "{cm:LaunchProgram,GitWolf}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; What an in-app update may have left. Nothing else is removed that the installer didn't put there.
Type: filesandordirs; Name: "{app}\.gitwolf-update-new"
Type: filesandordirs; Name: "{app}\.gitwolf-update-old"

[Code]
// Settings can register gitwolf:// links and the gitwolf command. Left behind, they would start
// whatever is later put where the program was. Only ones that point at this installation are removed.
procedure RemoveOutsideAccess;
var
  Installed, Command, CommandFile: String;
  Script: AnsiString;
begin
  Installed := '"' + Lowercase(ExpandConstant('{app}\{#Program}')) + '"';
  if RegQueryStringValue(HKCU, 'Software\Classes\gitwolf\shell\open\command', '', Command)
    and (Pos(Installed, Lowercase(Command)) = 1) then
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\gitwolf');

  CommandFile := ExpandConstant('{localappdata}\Microsoft\WindowsApps\gitwolf.cmd');
  if LoadStringFromFile(CommandFile, Script)
    and (Pos('rem Installed by GitWolf', String(Script)) > 0)
    and (Pos(Installed, Lowercase(String(Script))) > 0) then
    DeleteFile(CommandFile);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveOutsideAccess;
end;
