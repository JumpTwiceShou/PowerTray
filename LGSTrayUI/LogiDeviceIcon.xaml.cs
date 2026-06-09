using Hardcodet.Wpf.TaskbarNotification;
using LGSTrayCore;
using LGSTrayPrimitives;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LGSTrayUI
{
    public class LogiDeviceIconFactory
    {
        private readonly AppSettings _appSettings;
        private readonly UserSettingsWrapper _userSettings;
        private readonly AlertStateService _alertState;

        public LogiDeviceIconFactory(IOptions<AppSettings> appSettings, UserSettingsWrapper userSettings, AlertStateService alertState)
        {
            _appSettings = appSettings.Value;
            _userSettings = userSettings;
            _alertState = alertState;
        }

        public LogiDeviceIcon CreateDeviceIcon(LogiDevice device, Action<LogiDeviceIcon>? config = null)
        {
            LogiDeviceIcon output = new(device, _appSettings, _userSettings, _alertState);
            config?.Invoke(output);

            return output;
        }
    }

    public partial class LogiDeviceIcon : UserControl, IDisposable
    {
        #region IDisposable
        private bool disposedValue;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    RemoveActiveIcon(this);
                    SubRef();
                    _blinkTimer.Stop();
                    _alertState.Changed -= OnAlertStateChanged;
                    _device.PropertyChanged -= LogiDevicePropertyChanged;
                    _userSettings.PropertyChanged -= NotifyIconViewModelPropertyChanged;
                    _userSettings.DeviceSettingsChanged -= UserSettingsDeviceSettingsChanged;
                    CheckTheme.StaticPropertyChanged -= CheckThemePropertyChanged;
                    taskbarIcon.TrayToolTipOpen -= OnTrayToolTipOpen;
                    taskbarIcon.TrayToolTipClose -= OnTrayToolTipClose;
                    _tooltipCloseTimer.Stop();
                    CloseTrayToolTip();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
                taskbarIcon.Dispose();
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~LogiDeviceIcon()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion

        private static int _refCount = 0;
        public static int RefCount => _refCount;

        public static void AddRef()
        {
            _refCount++;
            RefCountChanged?.Invoke(RefCount);
        }

        public static void SubRef()
        {
            _refCount--;
            RefCountChanged?.Invoke(RefCount);
        }

        public static event Action<int>? RefCountChanged;
        private static readonly List<LogiDeviceIcon> ActiveIcons = [];
        private static readonly object ActiveIconsLock = new();

        private Action<TaskbarIcon, LogiDevice> _drawBatteryIcon = BatteryIconDrawing.DrawIcon;
        private readonly AlertStateService _alertState;
        private readonly LogiDevice _device;
        private readonly UserSettingsWrapper _userSettings;
        private readonly DispatcherTimer _blinkTimer;
        private readonly DispatcherTimer _tooltipCloseTimer;
        private bool _blinkVisible = true;

        public LogiDeviceIcon(LogiDevice device, AppSettings appSettings, UserSettingsWrapper userSettings, AlertStateService alertState)
        {
            InitializeComponent();

            _device = device;
            _userSettings = userSettings;
            AddRef();
            AddActiveIcon(this);

            DataContext = device;
            _alertState = alertState;
            _alertState.Changed += OnAlertStateChanged;

            device.PropertyChanged += LogiDevicePropertyChanged;
            userSettings.PropertyChanged += NotifyIconViewModelPropertyChanged;
            userSettings.DeviceSettingsChanged += UserSettingsDeviceSettingsChanged;
            CheckTheme.StaticPropertyChanged += CheckThemePropertyChanged;
            RefreshDrawBatteryIcon();
            _blinkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500),
            };
            _tooltipCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4),
            };
            _tooltipCloseTimer.Tick += (_, _) => CloseTrayToolTip();
            taskbarIcon.TrayToolTipOpen += OnTrayToolTipOpen;
            taskbarIcon.TrayToolTipClose += OnTrayToolTipClose;
            _blinkTimer.Tick += (_, _) =>
            {
                _blinkVisible = !_blinkVisible;
                DrawBatteryIcon();
            };
            OnAlertStateChanged();
            DrawBatteryIcon();
        }

        private void OnTrayToolTipOpen(object sender, RoutedEventArgs e)
        {
            ConfigureTrayToolTip();
            _tooltipCloseTimer.Stop();
            _tooltipCloseTimer.Start();
        }

        private void OnTrayToolTipClose(object sender, RoutedEventArgs e)
        {
            _tooltipCloseTimer.Stop();
        }

        private void ConfigureTrayToolTip()
        {
            ToolTip? toolTip = taskbarIcon.TrayToolTipResolved;
            if (toolTip == null)
            {
                return;
            }

            toolTip.StaysOpen = false;
            ToolTipService.SetShowDuration(toolTip, 4000);
        }

        private void CloseTrayToolTip()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(CloseTrayToolTip);
                return;
            }

            _tooltipCloseTimer.Stop();
            ToolTip? toolTip = taskbarIcon.TrayToolTipResolved;
            if (toolTip != null)
            {
                toolTip.IsOpen = false;
            }
        }

        public static bool ShowBalloonOnFirstIcon(string title, string body)
        {
            LogiDeviceIcon? icon = null;
            lock (ActiveIconsLock)
            {
                foreach (LogiDeviceIcon activeIcon in ActiveIcons)
                {
                    if (!activeIcon.disposedValue)
                    {
                        icon = activeIcon;
                        break;
                    }
                }
            }

            if (icon == null)
            {
                return false;
            }

            NotificationService.ShowBalloon(icon.taskbarIcon, title, body);
            return true;
        }

        private static void AddActiveIcon(LogiDeviceIcon icon)
        {
            lock (ActiveIconsLock)
            {
                ActiveIcons.Add(icon);
            }
        }

        private static void RemoveActiveIcon(LogiDeviceIcon icon)
        {
            lock (ActiveIconsLock)
            {
                ActiveIcons.Remove(icon);
            }
        }

        private void OnAlertStateChanged()
        {
            bool shouldBlink = _alertState.IsBlinking(_device.DeviceId);
            if (shouldBlink && !_blinkTimer.IsEnabled)
            {
                _blinkVisible = true;
                _blinkTimer.Start();
            }
            else if (!shouldBlink && _blinkTimer.IsEnabled)
            {
                _blinkTimer.Stop();
                _blinkVisible = true;
            }

            DrawBatteryIcon();
        }

        private void NotifyIconViewModelPropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (s is not UserSettingsWrapper userSettings)
            {
                return;
            }

            if (e.PropertyName == nameof(UserSettingsWrapper.NumericDisplay))
            {
                RefreshDrawBatteryIcon();
                DrawBatteryIcon();
            }
        }

        private void UserSettingsDeviceSettingsChanged(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || deviceId == _device.DeviceId)
            {
                RefreshDrawBatteryIcon();
                DrawBatteryIcon();
            }
        }

        private void RefreshDrawBatteryIcon()
        {
            _drawBatteryIcon = _userSettings.GetDeviceNumericDisplay(_device.DeviceId)
                ? BatteryIconDrawing.DrawNumeric
                : BatteryIconDrawing.DrawIcon;
        }

        private void CheckThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(CheckTheme.TaskbarLightTheme) or nameof(CheckTheme.TaskbarThemeSuffix))
            {
                DrawBatteryIcon();
            }
        }

        private void LogiDevicePropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (s is not LogiDevice)
            {
                return;
            }
            else if (e.PropertyName is nameof(LogiDevice.BatteryPercentage)
                or nameof(LogiDevice.PowerSupplyStatus)
                or nameof(LogiDevice.DeviceType)
                or nameof(LogiDevice.DeviceId))
            {
                RefreshDrawBatteryIcon();
                DrawBatteryIcon();
            }
            else if (e.PropertyName == nameof(LogiDeviceViewModel.DisplayToolTipString))
            {
                CloseTrayToolTip();
            }
        }

        private void DrawBatteryIcon()
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (disposedValue)
                {
                    return;
                }

                CloseTrayToolTip();

                if (_alertState.IsBlinking(_device.DeviceId) && !_blinkVisible)
                {
                    BatteryIconDrawing.DrawAlert(taskbarIcon, _device);
                    return;
                }

                _drawBatteryIcon(taskbarIcon, _device);
            });
        }
    }
}
