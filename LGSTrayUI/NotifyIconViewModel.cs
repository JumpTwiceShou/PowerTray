using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LGSTrayCore;
using LGSTrayCore.Managers;
using LGSTrayPrimitives;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LGSTrayUI
{
    public partial class NotifyIconViewModel : ObservableObject, IHostedService
    {
        private readonly MainTaskbarIconWrapper _mainTaskbarIconWrapper;
        private readonly LocalizationService _loc;
        private readonly SettingsWindowFactory _settingsWindowFactory;
        private readonly AlertManager _alertManager;
        private readonly UpdateService _updateService;
        private readonly LogiDeviceCollection _deviceCollection;
        private readonly SemaphoreSlim _rediscoverSemaphore = new(1, 1);
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly TimeSpan _presencePeriod;
        private CancellationTokenSource? _presenceCts;
        private Task? _presenceTask;
        private Task? _updateTask;
        private Task? _manualRediscoverTask;
        private LogiDeviceViewModel? _menuDevice;

        public LocalizationService Loc => _loc;

        [ObservableProperty]
        private ObservableCollection<LogiDeviceViewModel> _logiDevices;

        private readonly UserSettingsWrapper _userSettings;
        public bool NumericDisplay
        {
            get
            {
                return _userSettings.NumericDisplay;
            }

            set
            {
                _userSettings.NumericDisplay = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MenuNumericDisplay));
            }
        }

        public bool MenuNumericDisplay => _menuDevice is { } device
            ? _userSettings.GetDeviceNumericDisplay(device.DeviceId)
            : _userSettings.NumericDisplay;

        public static string AssemblyVersion
        {
            get
            {
                string? version = Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    ?.Split('+')[0];
                return string.IsNullOrWhiteSpace(version) ? "Missing" : "v" + version;
            }
        }

        public bool AutoStart
        {
            get => _userSettings.AutoStart;
            set
            {
                _userSettings.AutoStart = value;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        private bool _rediscoverDevicesEnabled = true;

        private readonly IEnumerable<IDeviceManager> _deviceManagers;

        public NotifyIconViewModel(
            MainTaskbarIconWrapper mainTaskbarIconWrapper,
            ILogiDeviceCollection logiDeviceCollection,
            UserSettingsWrapper userSettings,
            IEnumerable<IDeviceManager> deviceManagers,
            LocalizationService loc,
            SettingsWindowFactory settingsWindowFactory,
            AlertManager alertManager,
            UpdateService updateService,
            IHostApplicationLifetime applicationLifetime,
            IOptions<AppSettings> appSettings
        )
        {
            _mainTaskbarIconWrapper = mainTaskbarIconWrapper;
            ((ContextMenu)Application.Current.FindResource("SysTrayMenu")).DataContext = this;

            _deviceCollection = (logiDeviceCollection as LogiDeviceCollection)!;
            _logiDevices = _deviceCollection.Devices;
            _userSettings = userSettings;
            _deviceManagers = deviceManagers;
            _loc = loc;
            _settingsWindowFactory = settingsWindowFactory;
            _alertManager = alertManager;
            _updateService = updateService;
            _applicationLifetime = applicationLifetime;
            _presencePeriod = TimeSpan.FromSeconds(appSettings.Value.Native.PresencePeriod);
            _alertManager.SetDevices(_logiDevices);
            _userSettings.PropertyChanged += UserSettingsPropertyChanged;
            _userSettings.DeviceSettingsChanged += UserSettingsDeviceSettingsChanged;
        }

        [RelayCommand]
        private void ExitApplication()
        {
            _applicationLifetime.StopApplication();
        }

        [RelayCommand]
        private void OpenSettings()
        {
            _settingsWindowFactory.Show();
        }

        [RelayCommand]
        private async Task CheckForUpdatesAsync()
        {
            await _updateService.CheckForUpdatesAsync();
        }

        [RelayCommand]
        private void ToggleNumericDisplay()
        {
            if (_menuDevice is { } device && _userSettings.HasDeviceNumericDisplayOverride(device.DeviceId))
            {
                _userSettings.SetDeviceNumericDisplayOverride(device.DeviceId, !_userSettings.GetDeviceNumericDisplay(device.DeviceId));
                OnPropertyChanged(nameof(MenuNumericDisplay));
                return;
            }

            NumericDisplay = !NumericDisplay;
        }

        [RelayCommand]
        private void ToggleAutoStart()
        {
            AutoStart = !AutoStart;
        }

        [RelayCommand]
        private void DeviceClicked(object? sender)
        {
            LogiDeviceViewModel? logiDevice = sender switch
            {
                LogiDeviceViewModel device => device,
                MenuItem { DataContext: LogiDeviceViewModel device } => device,
                _ => null
            };

            if (logiDevice == null) { return; }

            if (!logiDevice.IsChecked)
            {
                _userSettings.AddDevice(logiDevice.DeviceId);
                logiDevice.IsChecked = true;
            }
            else
            {
                _userSettings.RemoveDevice(logiDevice.DeviceId);
                logiDevice.IsChecked = false;
            }
        }

        [RelayCommand]
        private async Task RediscoverDevices()
        {
            CancellationToken cancellationToken = _presenceCts?.Token ?? CancellationToken.None;
            Task rediscoverTask = RunPresenceCheckAsync(cancellationToken);
            _manualRediscoverTask = rediscoverTask;
            try
            {
                await rediscoverTask;
            }
            finally
            {
                if (ReferenceEquals(_manualRediscoverTask, rediscoverTask))
                {
                    _manualRediscoverTask = null;
                }
            }
        }

        private async Task RunPresenceCheckAsync(CancellationToken cancellationToken)
        {
            if (!await _rediscoverSemaphore.WaitAsync(0, cancellationToken))
            {
                return;
            }

            try
            {
                RediscoverDevicesEnabled = false;
                await Task.WhenAll(_deviceManagers.Select(manager => manager.RediscoverDevicesAsync(cancellationToken)));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Manual device rediscover failed: {ex}");
            }
            finally
            {
                RediscoverDevicesEnabled = true;
                _rediscoverSemaphore.Release();
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _presenceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _presenceTask = Task.Run(() => PresenceLoopAsync(_presenceCts.Token), CancellationToken.None);
            _updateTask = Task.Run(() => AutoCheckForUpdatesAsync(_presenceCts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public void SetMenuDeviceContext(LogiDeviceViewModel? device)
        {
            _menuDevice = device;
            OnPropertyChanged(nameof(MenuNumericDisplay));
        }

        private void UserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UserSettingsWrapper.NumericDisplay))
            {
                OnPropertyChanged(nameof(MenuNumericDisplay));
            }
        }

        private void UserSettingsDeviceSettingsChanged(string deviceId)
        {
            if (_menuDevice == null || string.IsNullOrEmpty(deviceId) || deviceId == _menuDevice.DeviceId)
            {
                OnPropertyChanged(nameof(MenuNumericDisplay));
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _presenceCts?.Cancel();
            Task[] tasks =
            [
                _presenceTask ?? Task.CompletedTask,
                _updateTask ?? Task.CompletedTask,
                _manualRediscoverTask ?? Task.CompletedTask,
            ];
            try
            {
                await Task.WhenAll(tasks).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            _presenceTask = null;
            _updateTask = null;
            _manualRediscoverTask = null;
            _presenceCts?.Dispose();
            _presenceCts = null;
            _userSettings.PropertyChanged -= UserSettingsPropertyChanged;
            _userSettings.DeviceSettingsChanged -= UserSettingsDeviceSettingsChanged;
            _mainTaskbarIconWrapper.Dispose();
        }

        private async Task PresenceLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_presencePeriod, cancellationToken);
                    foreach (IDeviceManager manager in _deviceManagers)
                    {
                        try
                        {
                            await manager.CheckHealthAsync(cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Debug.WriteLine($"Device manager health check failed: {ex}");
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }
        }

        private async Task AutoCheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                if (_userSettings.AutoCheckUpdates)
                {
                    await _updateService.CheckForUpdatesAsync(showAlreadyLatest: false, showFailures: false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Automatic update check failed: {ex}");
            }
        }
    }
}
