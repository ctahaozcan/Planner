#ifndef MyAppVersion
  #define MyAppVersion "1.9.2"
#endif
#define MyAppName "Yaver"
#define MyAppExeName "Yaver.exe"
#define MyAppPublisher "Yaver"
; Single-instance mutex created by Yaver.exe (also --min tray). Not used as a
; blocking AppMutex: closing the window does not release it. [Code] detects
; Local\Yaver.SingleInstance and stops the process before file copy.
#define MyAppMutex "Local\Yaver.SingleInstance"

[Setup]
AppId={{C4E8A1B7-3F29-4D6C-9E12-7A5B8C0D1F34}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription=Yaver — Günlük asistan
DefaultDirName={localappdata}\Programs\Yaver
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=Yaver-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Planner.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
SetupLogging=yes
CloseApplications=yes
CloseApplicationsFilter=Yaver.exe,Planlayici.exe,*.dll
RestartApplications=yes
UsePreviousAppDir=no
UsePreviousTasks=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Ek görevler:"
Name: "startup"; Description: "Windows oturum açıldığında başlat"; GroupDescription: "Ek görevler:"; Flags: unchecked

[Files]
Source: "..\dist\Yaver\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace uninsrestartdelete

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Yaver'ı Kaldır"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{userdesktop}\Planlayıcı.lnk"
Type: files; Name: "{userprograms}\Planlayıcı.lnk"
Type: filesandordirs; Name: "{userprograms}\Planlayıcı"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Planlayici"; Flags: deletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Yaver"; ValueData: """{app}\{#MyAppExeName}"" --min"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} uygulamasını başlat"; Flags: nowait postinstall skipifsilent; WorkingDir: "{app}"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Yaver.exe /F /T"; Flags: runhidden; RunOnceId: "StopYaver"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Planlayici.exe /F /T"; Flags: runhidden; RunOnceId: "StopPlanlayici"

[Code]
function StopImage(const ImageName: String): Integer;
var
  ResultCode: Integer;
begin
  ResultCode := 0;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM ' + ImageName + ' /F /T',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode;
end;

procedure RequestCleanShutdownAt(const ExePath: String);
var
  ResultCode: Integer;
begin
  if (ExePath <> '') and FileExists(ExePath) then
    Exec(ExePath, '--shutdown', '', SW_HIDE, ewNoWait, ResultCode);
end;

function AppStillRunning: Boolean;
begin
  Result := CheckForMutexes('{#MyAppMutex}') or
            CheckForMutexes('Local\Planlayici.SingleInstance');
end;

function StopRunningApps: Boolean;
var
  I: Integer;
begin
  { app constant is not ready in InitializeSetup; use the per-user install path. }
  RequestCleanShutdownAt(ExpandConstant('{localappdata}\Programs\Yaver\{#MyAppExeName}'));
  RequestCleanShutdownAt(ExpandConstant('{localappdata}\Programs\Planlayici\Planlayici.exe'));
  Sleep(700);
  StopImage('Yaver.exe');
  StopImage('Planlayici.exe');

  for I := 1 to 24 do
  begin
    if not AppStillRunning then
    begin
      Sleep(400);
      Result := True;
      Exit;
    end;
    if (I = 6) or (I = 14) then
    begin
      StopImage('Yaver.exe');
      StopImage('Planlayici.exe');
    end;
    Sleep(250);
  end;

  Result := True;
end;

function InitializeSetup(): Boolean;
begin
  StopRunningApps();
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  RequestCleanShutdownAt(ExpandConstant('{app}\{#MyAppExeName}'));
  StopRunningApps();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    StopRunningApps();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopRunningApps();
end;
