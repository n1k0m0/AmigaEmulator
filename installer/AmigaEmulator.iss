#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#ifndef AppVersionInfo
#define AppVersionInfo "0.1.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts\installer"
#endif
#define AppGuid "7B9B8822-5AF7-4E06-B72B-72D658B6C5D9"
#define AppUninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{7B9B8822-5AF7-4E06-B72B-72D658B6C5D9}_is1"

[Setup]
AppId={{{#AppGuid}}
AppName=Amiga Emulator
AppVersion={#AppVersion}
AppVerName=Amiga Emulator {#AppVersion}
AppPublisher=Nils Kopal
AppPublisherURL=https://github.com/n1k0m0/AmigaEmulator
AppSupportURL=https://github.com/n1k0m0/AmigaEmulator/issues
AppUpdatesURL=https://github.com/n1k0m0/AmigaEmulator/releases
DefaultDirName={autopf}\AmigaEmulator
DefaultGroupName=Amiga Emulator
DisableProgramGroupPage=yes
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=AmigaEmulator-{#AppVersion}-win-x64-setup
SetupIconFile=..\AmigaEmulator\amiga.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
UsedUserAreasWarning=no
UninstallDisplayIcon={app}\AmigaEmulator.exe
VersionInfoVersion={#AppVersionInfo}
VersionInfoCompany=Nils Kopal
VersionInfoDescription=Amiga Emulator Setup
VersionInfoProductName=Amiga Emulator
VersionInfoProductVersion={#AppVersion}
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.rom,*.bin,*.key,*.adf,*.adz,*.dms,*.ipf,*.hdf,*.hdd,*.img,*.iso,*.vhd,*.vhdx,*.amigasav,*.sav,*.lha,*.lzh,*.zip,*.7z,*.rar,roms\*,media\*,games\*,saves\*,relay_server\*"

[Icons]
Name: "{group}\Amiga Emulator"; Filename: "{app}\AmigaEmulator.exe"; WorkingDir: "{app}"; Check: not WizardNoIcons
Name: "{autodesktop}\Amiga Emulator"; Filename: "{app}\AmigaEmulator.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\AmigaEmulator.exe"; Description: "{cm:LaunchProgram,Amiga Emulator}"; Flags: nowait postinstall skipifsilent

; All AppData, external ROM paths, saves and hard disk images are user-owned.
; Uninstall removes only installed application files, never those data directories.

[Code]
var
  PreviousInstallDir: string;

function FindPreviousInstallDir(var InstallDir: string): Boolean;
begin
  Result := RegQueryStringValue(HKLM, '{#AppUninstallKey}', 'InstallLocation', InstallDir);
  if not Result then
    Result := RegQueryStringValue(HKCU, '{#AppUninstallKey}', 'InstallLocation', InstallDir);
end;

function InitializeSetup(): Boolean;
var
  InstallDir: string;
begin
  Result := True;
  PreviousInstallDir := '';
  if (not WizardSilent) and FindPreviousInstallDir(InstallDir) and FileExists(AddBackslash(InstallDir) + 'AmigaEmulator.exe') then
  begin
    if MsgBox('A previous Amiga Emulator installation was found:' + #13#10 + #13#10 + InstallDir + #13#10 + #13#10 +
      'Do you want Setup to remove the old application files before installing this version?' + #13#10 + #13#10 +
      'Your ROMs, settings, saves and hard disk images are kept.', mbConfirmation, MB_YESNO) = IDYES then
      PreviousInstallDir := InstallDir;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) and (PreviousInstallDir <> '') then
  begin
    { Delete only known application files. Never recursively delete a user-chosen folder. }
    DeleteFile(AddBackslash(PreviousInstallDir) + 'AmigaEmulator.exe');
    DeleteFile(AddBackslash(PreviousInstallDir) + 'AmigaEmulator.dll');
    DeleteFile(AddBackslash(PreviousInstallDir) + 'AmigaEmulator.deps.json');
    DeleteFile(AddBackslash(PreviousInstallDir) + 'AmigaEmulator.runtimeconfig.json');
    DeleteFile(AddBackslash(PreviousInstallDir) + 'SharpPixels.dll');
  end;
end;
