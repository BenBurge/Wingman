; Per-user installer for Wingman, compiled by tools/Build-Installer.ps1 (Inno Setup 6.3 or later).
; The /D defines below come from that script; the defaults only make a bare ISCC run work.

#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef OutputName
  #define OutputName "wingman-v" + AppVersion + "-win-x64-setup"
#endif
#ifndef ArchAllowed
  #define ArchAllowed "x64compatible"
#endif

[Setup]
; The uninstall entry is keyed on this id; it must never change between releases.
AppId={{FC7D592F-76BD-4409-9F32-1C9EAF29A091}
AppName=Wingman
AppVersion={#AppVersion}
AppPublisher=Ben Burge
AppPublisherURL=https://github.com/BenBurge/Wingman
AppSupportURL=https://github.com/BenBurge/Wingman/issues
AppUpdatesURL=https://github.com/BenBurge/Wingman/releases
UninstallDisplayName=Wingman
UninstallDisplayIcon={app}\wingman.exe
DefaultDirName={localappdata}\Programs\Wingman
DisableDirPage=auto
DefaultGroupName=Wingman
; `wingman setup` creates the Start Menu shortcut, carrying the AppUserModelID toasts need.
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
; Empty so Setup never offers, or accepts, a machine-wide install.
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode={#ArchAllowed}
SetupIconFile=..\assets\wingman.ico
LicenseFile=..\LICENSE
ChangesEnvironment=yes
; CloseWingman in [Code] closes the tray and any wingman.exe running from {app}.
CloseApplications=no
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}

[Files]
Source: "{#SourceDir}\wingman.exe"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: "openapp"; Description: "Open Wingman when setup finishes"; Check: not WizardSilent
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Icons]
Name: "{userdesktop}\Wingman"; Filename: "{app}\wingman.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Check: NeedsAddPath(ExpandConstant('{app}'))

[Run]
Filename: "{app}\wingman.exe"; Parameters: "setup"; Flags: runhidden waituntilterminated; StatusMsg: "Registering scheduled checks and the tray..."
; Not skipifsilent: a silent upgrade (winget, self-update) closed the tray and must restart it.
Filename: "{sys}\conhost.exe"; Parameters: "--headless ""{app}\wingman.exe"" tray"; Flags: nowait runhidden; Check: ShouldStartTray
Filename: "{app}\wingman.exe"; Description: "Open Wingman"; Flags: nowait postinstall skipifsilent shellexec; Tasks: openapp

[Code]
const
  HWND_MESSAGE = -3;
  WM_CLOSE = $0010;
  TrayWindowClass = 'Wingman.Tray';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

// WindowName is a Longint so 0 can be passed as NULL, which matches any window title. Setup is a
// 32-bit process, so a Longint holds any window handle it can see.
function FindWindowEx(Parent, ChildAfter: Longint; ClassName: String; WindowName: Longint): Longint;
  external 'FindWindowExW@user32.dll stdcall';

function FindTrayWindow: Longint;
begin
  Result := FindWindowEx(HWND_MESSAGE, 0, TrayWindowClass, 0);
end;

// Asks the tray to quit, then stops whatever wingman.exe still runs from {app}, so the installer
// can replace the exe and the uninstaller can delete it. A wingman.exe elsewhere is left alone.
procedure CloseWingman;
var
  Tray: Longint;
  Waited: Integer;
  AppDir: String;
  Command: String;
  ResultCode: Integer;
begin
  Tray := FindTrayWindow;
  if Tray <> 0 then
  begin
    PostMessage(Tray, WM_CLOSE, 0, 0);
    Waited := 0;
    while (FindTrayWindow <> 0) and (Waited < 3000) do
    begin
      Sleep(100);
      Waited := Waited + 100;
    end;
  end;

  AppDir := ExpandConstant('{app}');
  StringChangeEx(AppDir, '''', '''''', True);
  // Win32_Process rather than Get-Process: a 32-bit PowerShell cannot read the path of a 64-bit
  // process through Get-Process, and which PowerShell {sys} resolves to depends on the mode.
  Command :=
    '-NoProfile -NonInteractive -Command "' +
    'Get-CimInstance Win32_Process | Where-Object { $_.Name -eq ''wingman.exe'' -and $_.ExecutablePath -and ' +
    '$_.ExecutablePath.StartsWith(''' + AppDir + '\'', [StringComparison]::OrdinalIgnoreCase) } | ' +
    'ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"';
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Command, '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
end;

function PathContains(Paths, Dir: String): Boolean;
var
  Padded: String;
begin
  Padded := ';' + Uppercase(Paths) + ';';
  Result :=
    (Pos(';' + Uppercase(Dir) + ';', Padded) > 0) or
    (Pos(';' + Uppercase(Dir) + '\;', Padded) > 0);
end;

function NeedsAddPath(Dir: String): Boolean;
var
  Paths: String;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Paths) then
    Result := True
  else
    Result := not PathContains(Paths, Dir);
end;

// Removes Dir with or without a trailing backslash, keeping every other entry, including empty
// ones, exactly as it was.
procedure RemovePath(Dir: String);
var
  Paths: String;
  Padded: String;
  Entry: String;
  P: Integer;
  Changed: Boolean;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Paths) then
    exit;

  Padded := ';' + Paths + ';';
  Changed := False;
  Entry := ';' + Uppercase(Dir) + ';';
  P := Pos(Entry, Uppercase(Padded));
  while P > 0 do
  begin
    // Deleting the leading separator and the folder leaves the trailing separator in place.
    Delete(Padded, P, Length(Entry) - 1);
    Changed := True;
    P := Pos(Entry, Uppercase(Padded));
  end;
  Entry := ';' + Uppercase(Dir) + '\;';
  P := Pos(Entry, Uppercase(Padded));
  while P > 0 do
  begin
    Delete(Padded, P, Length(Entry) - 1);
    Changed := True;
    P := Pos(Entry, Uppercase(Padded));
  end;

  if Changed then
  begin
    Paths := Copy(Padded, 2, Length(Padded) - 2);
    RegWriteExpandStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Paths);
  end;
end;

// `wingman setup` leaves the Run value out when the user turned the tray off, so an upgrade
// only restarts a tray the user wants. CI passes /notray=1 so no tray outlives the test.
function ShouldStartTray: Boolean;
begin
  Result :=
    (ExpandConstant('{param:notray|0}') <> '1') and
    RegValueExists(HKEY_CURRENT_USER, RunKey, 'Wingman');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseWingman;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  // usUninstall runs before any file is removed, so wingman.exe is still there to undo its own
  // registration. Settings and history in %APPDATA%\Wingman are kept for a reinstall.
  if CurUninstallStep = usUninstall then
  begin
    CloseWingman;
    Exec(ExpandConstant('{app}\wingman.exe'), 'setup --remove', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    RemovePath(ExpandConstant('{app}'));
  end;
end;
