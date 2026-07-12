#ifndef SourceDir
  #error SourceDir is required
#endif

#ifndef OutputDir
  #define OutputDir "bin\Release\installer"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "PowerTraySetup"
#endif

#ifndef AppVersion
  #define AppVersion "1.4.2"
#endif

#ifndef IncludeRuntime
  #define IncludeRuntime 0
#endif

#define AppName "PowerTray"
#define AppExeName "PowerTray.exe"
#define HidExeName "PowerTrayHID.exe"
#define AppUserModelID "PowerTray.NativeBattery"

[Setup]
AppId={{8F95D566-112A-4C24-BB15-03F79732A7F9}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=PowerTray
DefaultDirName={localappdata}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=LGSTrayUI\Resources\logo_black.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "PowerTrayInstaller\ChineseSimplified.isl"
Name: "japanese"; MessagesFile: "PowerTrayInstaller\Japanese.isl"

[CustomMessages]
english.StartWithWindows=Start with Windows
japanese.StartWithWindows=Windows 起動時に開始
chinesesimp.StartWithWindows=开机自启动
english.AutoCheckUpdates=Check for updates automatically
japanese.AutoCheckUpdates=アップデートを自動確認
chinesesimp.AutoCheckUpdates=自动检查更新
english.LaunchAfterInstall=Launch PowerTray after setup
japanese.LaunchAfterInstall=セットアップ完了後に PowerTray を起動
chinesesimp.LaunchAfterInstall=安装完成后启动 PowerTray
english.MissingRuntimeMessage=PowerTray requires the Microsoft .NET 8 Desktop Runtime x64, including Microsoft.NETCore.App 8.x and Microsoft.WindowsDesktop.App 8.x. The download page will open now. Please install the runtime, then run this setup again.
japanese.MissingRuntimeMessage=PowerTray には Microsoft .NET 8 Desktop Runtime x64 が必要です。Microsoft.NETCore.App 8.x と Microsoft.WindowsDesktop.App 8.x の両方が必要です。これからダウンロードページを開きます。ランタイムをインストールしてから、もう一度このセットアップを実行してください。
chinesesimp.MissingRuntimeMessage=PowerTray 需要 Microsoft .NET 8 Desktop Runtime x64，并且必须包含 Microsoft.NETCore.App 8.x 和 Microsoft.WindowsDesktop.App 8.x。现在会打开下载页面。请先安装运行时，然后重新运行此安装程序。

[Tasks]
Name: "autostart"; Description: "{cm:StartWithWindows}"
Name: "checkupdates"; Description: "{cm:AutoCheckUpdates}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\LGSTray.exe"
Type: files; Name: "{app}\LGSTrayHID.exe"
Type: files; Name: "{app}\LGSTray.dll.config"
Type: files; Name: "{autoprograms}\{#AppName}\{#AppName}.lnk"
Type: dirifempty; Name: "{autoprograms}\{#AppName}"
Type: files; Name: "{autoprograms}\LGSTray\LGSTray.lnk"
Type: dirifempty; Name: "{autoprograms}\LGSTray"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; AppUserModelID: "{#AppUserModelID}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\installer-edition.txt"
Type: dirifempty; Name: "{app}"
Type: files; Name: "{autoprograms}\{#AppName}\{#AppName}.lnk"
Type: dirifempty; Name: "{autoprograms}\{#AppName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\PowerTray"; Flags: deletekey noerror
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\LGSTray"; Flags: deletekey noerror

[Code]
const
  IncludeRuntime = {#IncludeRuntime};
  RuntimeUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0/runtime?cid=getdotnetcore&runtime=windowsdesktop&arch=x64';

function HasRuntime8InDirectory(FrameworkName: String; BaseDir: String): Boolean;
var
  FindRec: TFindRec;
  FrameworkDir: String;
begin
  Result := False;
  FrameworkDir := BaseDir + '\' + FrameworkName;
  if not DirExists(FrameworkDir) then
    Exit;

  if FindFirst(FrameworkDir + '\8.*', FindRec) then
  begin
    try
      repeat
        if DirExists(FrameworkDir + '\' + FindRec.Name) then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function HasRuntime8FromDotnet(FrameworkName: String; DotnetExe: String): Boolean;
var
  TempFile: String;
  Output: AnsiString;
  ResultCode: Integer;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\PowerTrayDotnetRuntimes.txt');
  DeleteFile(TempFile);

  if Exec(ExpandConstant('{sys}\cmd.exe'), '/C "' + DotnetExe + '" --list-runtimes > "' + TempFile + '" 2>NUL', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if (ResultCode = 0) and LoadStringFromFile(TempFile, Output) then
      Result := Pos(FrameworkName + ' 8.', Output) > 0;
  end;

  DeleteFile(TempFile);
end;

function HasRuntime8(FrameworkName: String): Boolean;
var
  Version: String;
  SharedDir: String;
begin
  Result := False;
  SharedDir := ExpandConstant('{commonpf64}\dotnet\shared');

  if RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\' + FrameworkName, 'Version', Version)
    and (Copy(Version, 1, 2) = '8.') then
  begin
    Result := True;
    Exit;
  end;

  if HasRuntime8InDirectory(FrameworkName, SharedDir) then
  begin
    Result := True;
    Exit;
  end;

  if HasRuntime8InDirectory(FrameworkName, ExpandConstant('{localappdata}\Microsoft\dotnet\shared')) then
  begin
    Result := True;
    Exit;
  end;

  if FileExists(ExpandConstant('{commonpf64}\dotnet\dotnet.exe')) and
     HasRuntime8FromDotnet(FrameworkName, ExpandConstant('{commonpf64}\dotnet\dotnet.exe')) then
  begin
    Result := True;
    Exit;
  end;

  if FileExists(ExpandConstant('{localappdata}\Microsoft\dotnet\dotnet.exe')) and
     HasRuntime8FromDotnet(FrameworkName, ExpandConstant('{localappdata}\Microsoft\dotnet\dotnet.exe')) then
  begin
    Result := True;
    Exit;
  end;
end;

function HasRequiredDotnet8(): Boolean;
begin
  Result := HasRuntime8('Microsoft.NETCore.App') and HasRuntime8('Microsoft.WindowsDesktop.App');
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if (IncludeRuntime = 0) and (not HasRequiredDotnet8()) then
  begin
    MsgBox(ExpandConstant('{cm:MissingRuntimeMessage}'), mbCriticalError, MB_OK);
    ShellExec('open', RuntimeUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end;
end;

procedure StopInstalledProcess(ExeName: String; RequestGracefulShutdown: Boolean);
var
  TargetPath: String;
  PowerShellPath: String;
  ScriptPath: String;
  Script: String;
  ResultCode: Integer;
begin
  TargetPath := ExpandConstant('{app}\') + ExeName;
  if not FileExists(TargetPath) then
    Exit;

  if RequestGracefulShutdown then
  begin
    Exec(TargetPath, '--shutdown', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2500);
  end;

  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  if not FileExists(PowerShellPath) then
    Exit;

  ScriptPath := ExpandConstant('{tmp}\PowerTrayStopInstalledProcess.ps1');
  Script :=
    'param([string]$TargetPath,[string]$ProcessName)' + #13#10 +
    '$expected = [IO.Path]::GetFullPath($TargetPath)' + #13#10 +
    'Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ProcessName)) -ErrorAction SilentlyContinue | ForEach-Object {' + #13#10 +
    '  try {' + #13#10 +
    '    if ($_.Path -and ([IO.Path]::GetFullPath($_.Path) -eq $expected)) {' + #13#10 +
    '      Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue' + #13#10 +
    '    }' + #13#10 +
    '  } catch {}' + #13#10 +
    '}' + #13#10;
  SaveStringToFile(ScriptPath, Script, False);
  Exec(
    PowerShellPath,
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '" "' + TargetPath + '" "' + ExeName + '"',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode
  );
  DeleteFile(ScriptPath);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopInstalledProcess('{#AppExeName}', True);
  StopInstalledProcess('{#HidExeName}', False);
  StopInstalledProcess('LGSTray.exe', False);
  StopInstalledProcess('LGSTrayHID.exe', False);
  Result := '';
end;

function SelectedLanguageCode(): String;
begin
  if ActiveLanguage() = 'chinesesimp' then
    Result := 'zh-CN'
  else if ActiveLanguage() = 'japanese' then
    Result := 'ja-JP'
  else
    Result := 'en-US';
end;

function BoolJson(Value: Boolean): String;
begin
  if Value then
    Result := 'true'
  else
    Result := 'false';
end;

procedure WriteInitialSettings();
var
  SettingsDir: String;
  SettingsPath: String;
  Json: String;
begin
  SettingsDir := ExpandConstant('{userappdata}\PowerTray');
  SettingsPath := SettingsDir + '\settings.json';
  if FileExists(SettingsPath) then
    Exit;

  ForceDirectories(SettingsDir);
  Json :=
    '{' + #13#10 +
    '  "SchemaVersion": 1,' + #13#10 +
    '  "Language": "' + SelectedLanguageCode() + '",' + #13#10 +
    '  "ThemeMode": "system",' + #13#10 +
    '  "NumericDisplay": false,' + #13#10 +
    '  "AutoStart": ' + BoolJson(WizardIsTaskSelected('autostart')) + ',' + #13#10 +
    '  "AutoCheckUpdates": ' + BoolJson(WizardIsTaskSelected('checkupdates')) + ',' + #13#10 +
    '  "SelectedDevices": [],' + #13#10 +
    '  "GlobalAlerts": {' + #13#10 +
    '    "ThresholdPercent": 15,' + #13#10 +
    '    "WindowsNotification": true,' + #13#10 +
    '    "TrayBlink": true,' + #13#10 +
    '    "QuietHoursEnabled": false,' + #13#10 +
    '    "QuietHoursStart": "23:00",' + #13#10 +
    '    "QuietHoursEnd": "08:00",' + #13#10 +
    '    "SuppressNotificationsWhenFullscreen": true' + #13#10 +
    '  },' + #13#10 +
    '  "Devices": {}' + #13#10 +
    '}' + #13#10;
  SaveStringToFile(SettingsPath, Json, False);
end;

procedure WriteAutoStart();
var
  RunKey: String;
begin
  RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
  if WizardIsTaskSelected('autostart') then
    RegWriteStringValue(HKCU, RunKey, 'PowerTray', '"' + ExpandConstant('{app}\{#AppExeName}') + '"')
  else
    RegDeleteValue(HKCU, RunKey, 'PowerTray');
end;

procedure WriteInstallerEdition();
var
  Edition: String;
begin
  if IncludeRuntime = 1 then
    Edition := 'full'
  else
    Edition := 'light';

  SaveStringToFile(ExpandConstant('{app}\installer-edition.txt'), Edition + #13#10, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteInitialSettings();
    WriteAutoStart();
    WriteInstallerEdition();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopInstalledProcess('{#AppExeName}', True);
    StopInstalledProcess('{#HidExeName}', False);
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PowerTray');
  end;
end;
