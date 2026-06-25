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
  #define AppVersion "1.4.1"
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
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autostartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
