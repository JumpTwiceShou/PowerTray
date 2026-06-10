using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LGSTrayCore;
using LGSTrayCore.Managers;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private CancellationTokenSource? _presenceCts;

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
            }
        }

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
            UpdateService updateService
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
            _alertManager.SetDevices(_logiDevices);
        }

        [RelayCommand]
        private static void ExitApplication()
        {
            Environment.Exit(0);
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
            await RunPresenceCheckAsync(manual: true, CancellationToken.None);
        }

        private async Task RunPresenceCheckAsync(bool manual, CancellationToken cancellationToken)
        {
            if (!await _rediscoverSemaphore.WaitAsync(0, cancellationToken))
            {
                return;
            }

            try
            {
                if (manual)
                {
                    RediscoverDevicesEnabled = false;
                }

                long epoch = _deviceCollection.BeginPresenceCheck();
                foreach (var manager in _deviceManagers)
                {
                    manager.RediscoverDevices();
                }

                await Task.Delay(TimeSpan.FromSeconds(12), cancellationToken);
                _deviceCollection.CompletePresenceCheck(epoch, manual ? 1 : 2);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (manual)
                {
                    RediscoverDevicesEnabled = true;
                }

                _rediscoverSemaphore.Release();
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _presenceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = Task.Run(() => PresenceLoopAsync(_presenceCts.Token), CancellationToken.None);
            _ = Task.Run(() => AutoCheckForUpdatesAsync(_presenceCts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _presenceCts?.Cancel();
            _presenceCts?.Dispose();
            _presenceCts = null;
            _mainTaskbarIconWrapper.Dispose();
            return Task.CompletedTask;
        }

        private async Task PresenceLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    await RunPresenceCheckAsync(manual: false, cancellationToken);
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
        }
    }
}
