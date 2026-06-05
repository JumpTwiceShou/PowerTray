using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PowerTrayInstaller;

internal static class Program
{
    private const string AppName = "PowerTray";
    private const string MainExe = "PowerTray.exe";
    private const string AutoStartRegValue = "PowerTray";
    private const string AppUserModelId = "PowerTray.NativeBattery";

    [STAThread]
    private static void Main()
    {
        string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (args.Any(x => x.Equals("--silent", StringComparison.OrdinalIgnoreCase)))
        {
            string language = ReadArg(args, "--language", "en-US");
            string installDir = ReadArg(
                args,
                "--dir",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName)
            );
            bool autoStart = bool.TryParse(ReadArg(args, "--autostart", "false"), out bool parsedAutoStart) && parsedAutoStart;
            bool launch = bool.TryParse(ReadArg(args, "--launch", "false"), out bool parsedLaunch) && parsedLaunch;
            InstallerForm.InstallCoreAsync(installDir, language, autoStart, launch, Console.WriteLine).GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }

    private static string ReadArg(string[] args, string key, string fallback)
    {
        int index = Array.FindIndex(args, x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    private sealed class InstallerForm : Form
    {
        private readonly ComboBox _language = new();
        private readonly TextBox _installPath = new();
        private readonly CheckBox _autoStart = new();
        private readonly CheckBox _launch = new();
        private readonly Button _browse = new();
        private readonly Button _install = new();
        private readonly Label _title = new();
        private readonly Label _pathLabel = new();
        private readonly Label _languageLabel = new();
        private readonly TextBox _log = new();

        public InstallerForm()
        {
            Text = "PowerTray Setup";
            Width = 620;
            Height = 430;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            _language.DropDownStyle = ComboBoxStyle.DropDownList;
            _language.Items.Add(new LanguageItem("en-US", "English"));
            _language.Items.Add(new LanguageItem("zh-CN", "简体中文"));
            _language.SelectedIndex = 0;
            _language.SelectedIndexChanged += (_, _) => ApplyLanguage();

            _installPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppName
            );
            _browse.Click += (_, _) => BrowseInstallPath();
            _install.Click += async (_, _) => await InstallAsync();

            _title.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;

            Controls.AddRange([
                _title,
                _languageLabel,
                _language,
                _pathLabel,
                _installPath,
                _browse,
                _autoStart,
                _launch,
                _install,
                _log
            ]);

            LayoutControls();
            ApplyLanguage();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutControls();
        }

        private void LayoutControls()
        {
            int margin = 24;
            int width = ClientSize.Width - (margin * 2);
            _title.SetBounds(margin, 20, width, 36);
            _languageLabel.SetBounds(margin, 72, 160, 24);
            _language.SetBounds(margin + 170, 70, 180, 28);
            _pathLabel.SetBounds(margin, 112, width, 24);
            _installPath.SetBounds(margin, 140, width - 92, 28);
            _browse.SetBounds(ClientSize.Width - margin - 82, 139, 82, 30);
            _autoStart.SetBounds(margin, 178, width, 28);
            _launch.SetBounds(margin, 210, width, 28);
            _install.SetBounds(margin, 250, 150, 34);
            _log.SetBounds(margin, 300, width, ClientSize.Height - 324);
        }

        private void ApplyLanguage()
        {
            bool zh = SelectedLanguage == "zh-CN";
            _title.Text = zh ? "安装 PowerTray" : "Install PowerTray";
            _languageLabel.Text = zh ? "安装语言" : "Language";
            _pathLabel.Text = zh ? "安装位置" : "Install location";
            _browse.Text = zh ? "浏览..." : "Browse...";
            _autoStart.Text = zh ? "开机自启" : "Start with Windows";
            _launch.Text = zh ? "安装完成后启动 PowerTray" : "Launch PowerTray after installation";
            _install.Text = zh ? "安装" : "Install";
            _launch.Checked = true;
        }

        private string SelectedLanguage => ((LanguageItem)_language.SelectedItem!).Code;

        private void BrowseInstallPath()
        {
            using FolderBrowserDialog dialog = new()
            {
                SelectedPath = _installPath.Text,
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _installPath.Text = dialog.SelectedPath;
            }
        }

        private async Task InstallAsync()
        {
            _install.Enabled = false;
            try
            {
                string target = _installPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(target))
                {
                    return;
                }

                await InstallCoreAsync(target, SelectedLanguage, _autoStart.Checked, _launch.Checked, Log);

                MessageBox.Show(this, SelectedLanguage == "zh-CN" ? "安装完成。" : "Installation complete.", AppName);
                Close();
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _install.Enabled = true;
            }
        }

        public static async Task InstallCoreAsync(string target, string language, bool autoStart, bool launch, Action<string> log)
        {
            log("Stopping running processes...");
            StopProcess("PowerTray");
            StopProcess("PowerTrayHID");
            StopProcess("LGSTray");
            StopProcess("LGSTrayHID");

            Directory.CreateDirectory(target);
            log("Extracting files...");
            await ExtractPayloadAsync(target);

            log("Writing settings...");
            WriteInitialSettings(language, autoStart);

            log("Creating shortcuts...");
            CreateShortcuts(target);
            WriteAutoStart(autoStart, Path.Combine(target, MainExe));

            log("Writing uninstall entry...");
            WriteUninstaller(target);
            WriteUninstallEntry(target);

            log("Installation complete.");
            if (launch)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(target, MainExe),
                    WorkingDirectory = target,
                    UseShellExecute = true,
                });
            }
        }

        private static async Task ExtractPayloadAsync(string target)
        {
            using Stream? resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("PowerTrayPayload.zip");
            if (resource == null)
            {
                throw new InvalidOperationException("Installer payload is missing.");
            }

            string tempZip = Path.Combine(Path.GetTempPath(), "PowerTrayPayload.zip");
            await using (FileStream file = File.Create(tempZip))
            {
                await resource.CopyToAsync(file);
            }

            ZipFile.ExtractToDirectory(tempZip, target, true);
            File.Delete(tempZip);
        }

        private static void WriteInitialSettings(string language, bool autoStart)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "settings.json");
            if (File.Exists(path))
            {
                return;
            }

            object settings = new
            {
                SchemaVersion = 1,
                Language = language,
                NumericDisplay = false,
                AutoStart = autoStart,
                SelectedDevices = Array.Empty<string>(),
                GlobalAlerts = new
                {
                    ThresholdPercent = 15,
                    WindowsNotification = true,
                    TrayBlink = true,
                    QuietHoursEnabled = false,
                    QuietHoursStart = "23:00",
                    QuietHoursEnd = "08:00",
                    SuppressNotificationsWhenFullscreen = true,
                },
                Devices = new { },
            };
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void CreateShortcuts(string target)
        {
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs",
                AppName
            );
            Directory.CreateDirectory(startMenu);
            string shortcut = Path.Combine(startMenu, AppName + ".lnk");
            CreateShortcut(shortcut, Path.Combine(target, MainExe), target);
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"), Path.Combine(target, MainExe), target);
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
        {
            object linkObject = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClsid)!)!;
            IShellLinkW link = (IShellLinkW)linkObject;
            link.SetPath(targetPath);
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription(AppName);

            try
            {
                IPropertyStore propertyStore = (IPropertyStore)linkObject;
                using PropVariant appId = PropVariant.FromString(AppUserModelId);
                propertyStore.SetValue(AppUserModelIdKey, appId);
                propertyStore.Commit();
            }
            catch
            {
                // Some Windows scripting hosts expose ShellLink without IPropertyStore.
                // The installer must still succeed; tray notifications remain available.
            }

            IPersistFile file = (IPersistFile)linkObject;
            file.Save(shortcutPath, true);
        }

        private static void WriteAutoStart(bool enabled, string exePath)
        {
            using RegistryKey? registry = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (registry == null)
            {
                return;
            }

            registry.DeleteValue("LGSTrayGUI", false);
            if (enabled)
            {
                registry.SetValue(AutoStartRegValue, $"\"{exePath}\"");
            }
            else
            {
                registry.DeleteValue(AutoStartRegValue, false);
            }
        }

        private static void WriteUninstallEntry(string target)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PowerTray");
            string uninstallScript = Path.Combine(target, "UninstallPowerTray.ps1");
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayIcon", Path.Combine(target, MainExe));
            key.SetValue("InstallLocation", target);
            key.SetValue("Publisher", "PowerTray");
            key.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{uninstallScript}\"");
        }

        private static void WriteUninstaller(string target)
        {
            string script = $$"""
$ErrorActionPreference = 'SilentlyContinue'
Get-Process PowerTray,PowerTrayHID,LGSTray,LGSTrayHID | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'PowerTray','LGSTrayGUI'
Remove-Item -LiteralPath "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\PowerTray" -Recurse -Force
Remove-Item -LiteralPath "$env:USERPROFILE\Desktop\PowerTray.lnk" -Force
Remove-Item -LiteralPath 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PowerTray' -Recurse -Force
Start-Sleep -Milliseconds 500
Remove-Item -LiteralPath '{{target}}' -Recurse -Force
""";
            File.WriteAllText(Path.Combine(target, "UninstallPowerTray.ps1"), script);
        }

        private static void StopProcess(string name)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try { process.Kill(); process.WaitForExit(3000); } catch { }
            }
        }

        private void Log(string message)
        {
            _log.AppendText(message + Environment.NewLine);
        }

        private sealed record LanguageItem(string Code, string Name)
        {
            public override string ToString() => Name;
        }
    }

    private static readonly PropertyKey AppUserModelIdKey = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };

    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(IntPtr pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(IntPtr pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory(IntPtr pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments(IntPtr pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(IntPtr pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000138-0000-0000-C000-000000000046")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(PropertyKey key, PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class PropVariant : IDisposable
    {
        private ushort vt;
        private ushort wReserved1;
        private ushort wReserved2;
        private ushort wReserved3;
        private IntPtr p;
        private int p2;

        public static PropVariant FromString(string value)
        {
            return new()
            {
                vt = 31,
                p = Marshal.StringToCoTaskMemUni(value),
            };
        }

        public void Dispose()
        {
            if (p != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(p);
                p = IntPtr.Zero;
            }
        }
    }
}
