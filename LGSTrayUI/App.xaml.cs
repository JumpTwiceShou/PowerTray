using LGSTrayCore;
using LGSTrayCore.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using System;
using LGSTrayPrimitives.IPC;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using LGSTrayPrimitives;
using Tommy.Extensions.Configuration;

using static LGSTrayUI.AppExtensions;
using System.Threading.Tasks;

namespace LGSTrayUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\PowerTray.NativeBattery.Instance";
    private const string ShowSettingsEventName = @"Local\PowerTray.NativeBattery.ShowSettings";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSettingsEvent;
    private RegisteredWaitHandle? _showSettingsRegistration;

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (!TryAcquireSingleInstance(e.Args))
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ThemeService.ApplyCurrentResources();

        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);

        EnableEfficiencyMode();

        var builder = Host.CreateEmptyApplicationBuilder(null);
        await LoadAppSettings(builder.Configuration);

        builder.Services.Configure<AppSettings>(builder.Configuration);
        builder.Services.AddLGSMessagePipe(true);
        builder.Services.AddSingleton<UserSettingsWrapper>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<LocalizationService>();
        builder.Services.AddSingleton<UpdateService>();
        builder.Services.AddSingleton<NativeDiagnosticsClient>();
        builder.Services.AddSingleton<AlertStateService>();
        builder.Services.AddSingleton<SystemStateService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<AlertManager>();

        builder.Services.AddSingleton<LogiDeviceIconFactory>();
        builder.Services.AddSingleton<LogiDeviceViewModelFactory>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<SettingsWindowFactory>();

        builder.Services.AddWebserver(builder.Configuration);

        builder.Services.AddIDeviceManager<LGSTrayHIDManager>(builder.Configuration);
        builder.Services.AddIDeviceManager<GHubManager>(builder.Configuration);
        builder.Services.AddSingleton<ILogiDeviceCollection, LogiDeviceCollection>();

        builder.Services.AddSingleton<MainTaskbarIconWrapper>();
        builder.Services.AddHostedService<NotifyIconViewModel>();

        var host = builder.Build();
        var loc = host.Services.GetRequiredService<LocalizationService>();
        ThemedMessageBox.Translate = key => loc[key];
        _ = host.Services.GetRequiredService<ThemeService>();
        RegisterShowSettingsSignal(host.Services.GetRequiredService<SettingsWindowFactory>());
        if (e.Args.Any(x => x.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
        {
            host.Services.GetRequiredService<SettingsWindowFactory>().Show();
        }

        try
        {
            await host.RunAsync();
        }
        finally
        {
            CleanupSingleInstance();
            Dispatcher.InvokeShutdown();
        }
    }

    private bool TryAcquireSingleInstance(string[] args)
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (createdNew)
        {
            _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
            return true;
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;

        if (args.Any(x => x.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
        {
            using EventWaitHandle showSettingsEvent = new(false, EventResetMode.AutoReset, ShowSettingsEventName);
            showSettingsEvent.Set();
        }

        return false;
    }

    private void RegisterShowSettingsSignal(SettingsWindowFactory settingsWindowFactory)
    {
        if (_showSettingsEvent == null)
        {
            return;
        }

        _showSettingsRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showSettingsEvent,
            (_, _) => Dispatcher.BeginInvoke(settingsWindowFactory.Show),
            null,
            Timeout.Infinite,
            false
        );
    }

    private void CleanupSingleInstance()
    {
        _showSettingsRegistration?.Unregister(null);
        _showSettingsRegistration = null;
        _showSettingsEvent?.Dispose();
        _showSettingsEvent = null;
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }

    static async Task LoadAppSettings(Microsoft.Extensions.Configuration.ConfigurationManager config)
    {
        try
        {
            config.AddTomlFile("appsettings.toml");
        }
        catch (Exception ex)
        {
            if (ex is FileNotFoundException || ex is InvalidDataException)
            {
                var msgBoxRet = ThemedMessageBox.Show(
                    "Could not read appsettings.toml. Reset it to defaults?",
                    "PowerTray - Settings Error",
                    MessageBoxButton.YesNo, MessageBoxResult.No
                );

                if (msgBoxRet == MessageBoxResult.Yes)
                {
                    await File.WriteAllBytesAsync(
                        Path.Combine(AppContext.BaseDirectory, "appsettings.toml"),
                        LGSTrayUI.Properties.Resources.defaultAppsettings
                    );
                }

                config.AddTomlFile("appsettings.toml");
            }
            else
            {
                throw;
            }
        }
    }

    private void CrashHandler(object sender, UnhandledExceptionEventArgs args)
    {
        Exception e = (Exception)args.ExceptionObject;
        long unixTime = DateTimeOffset.Now.ToUnixTimeSeconds();

        using StreamWriter writer = new($"./crashlog_{unixTime}.log", false);
        writer.WriteLine(e.ToString());
    }
}
