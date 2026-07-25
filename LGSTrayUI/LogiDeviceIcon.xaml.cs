using Hardcodet.Wpf.TaskbarNotification;
using LGSTrayCore;
using LGSTrayPrimitives;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace LGSTrayUI
{
    public class LogiDeviceIconFactory
    {
        private readonly AppSettings _appSettings;
        private readonly UserSettingsWrapper _userSettings;
        private readonly AlertStateService _alertState;
        private readonly TrayToolTipMode _effectiveTrayToolTipMode;

        public LogiDeviceIconFactory(IOptions<AppSettings> appSettings, UserSettingsWrapper userSettings, AlertStateService alertState)
        {
            _appSettings = appSettings.Value;
            _userSettings = userSettings;
            _alertState = alertState;
            _effectiveTrayToolTipMode = userSettings.EffectiveTrayToolTipMode;
        }

        public LogiDeviceIcon CreateDeviceIcon(LogiDevice device, Action<LogiDeviceIcon>? config = null)
        {
            LogiDeviceIcon output = new(
                device,
                _appSettings,
                _userSettings,
                _alertState,
                _effectiveTrayToolTipMode
            );
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
                disposedValue = true;

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
                    if (_toolTipRegistration.SubscribesCustomOpenEvent)
                    {
                        taskbarIcon.PreviewTrayToolTipOpen -= OnPreviewTrayToolTipOpen;
                    }
                    TrayContextMenuPlacement.Detach(taskbarIcon);
                    BindingOperations.ClearBinding(taskbarIcon, TaskbarIcon.ToolTipTextProperty);
                    taskbarIcon.ToolTipText = string.Empty;
                    if (_toolTipRegistration.UsesCustomContent)
                    {
                        TrayToolTipLifecycle.CloseBeforeIconDisposal(taskbarIcon);
                    }
                    else
                    {
                        taskbarIcon.TrayToolTip = null;
                    }
                    taskbarIcon.Dispose();
                    _customTrayToolTipBorder = null;
                    _customTrayToolTipText = null;
                }
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
        private bool _blinkVisible = true;
        private TrayToolTipRegistration _toolTipRegistration;
        private Border? _customTrayToolTipBorder;
        private TextBlock? _customTrayToolTipText;

        internal TaskbarIcon TaskbarIconForTesting => taskbarIcon;
        internal TrayToolTipRegistration ToolTipRegistrationForTesting => _toolTipRegistration;

        public LogiDeviceIcon(
            LogiDevice device,
            AppSettings appSettings,
            UserSettingsWrapper userSettings,
            AlertStateService alertState,
            TrayToolTipMode effectiveTrayToolTipMode
        )
        {
            InitializeComponent();

            _device = device;
            _userSettings = userSettings;
            AddRef();
            AddActiveIcon(this);

            DataContext = device;
            taskbarIcon.DataContext = device;
            ConfigureTrayToolTip(effectiveTrayToolTipMode);
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
            TrayContextMenuPlacement.Attach(taskbarIcon);
            _blinkTimer.Tick += (_, _) =>
            {
                _blinkVisible = !_blinkVisible;
                DrawBatteryIcon();
            };
            OnAlertStateChanged();
            DrawBatteryIcon();
        }

        private void ConfigureTrayToolTip(TrayToolTipMode mode)
        {
            _toolTipRegistration = TrayToolTipRegistration.For(mode);
            taskbarIcon.TrayToolTip = null;
            taskbarIcon.ToolTipText = string.Empty;

            if (_toolTipRegistration.UsesNativeText)
            {
                BindingOperations.SetBinding(
                    taskbarIcon,
                    TaskbarIcon.ToolTipTextProperty,
                    new Binding(nameof(LogiDeviceViewModel.NativeToolTipString))
                    {
                        Mode = BindingMode.OneWay,
                    }
                );
                return;
            }

            if (_toolTipRegistration.UsesCustomContent)
            {
                if (Resources["PowerTrayCustomTrayToolTipContent"] is not Border customContent
                    || customContent.Child is not TextBlock customText)
                {
                    throw new InvalidOperationException("The PowerTray custom tray tooltip resource is missing or invalid.");
                }

                _customTrayToolTipBorder = customContent;
                _customTrayToolTipText = customText;
                ApplyCustomTrayToolTipTheme();
                taskbarIcon.TrayToolTip = customContent;
                taskbarIcon.PreviewTrayToolTipOpen += OnPreviewTrayToolTipOpen;
            }
        }

        private void OnPreviewTrayToolTipOpen(object sender, RoutedEventArgs e)
        {
            ApplyCustomTrayToolTipTheme();

            ToolTip? toolTip = taskbarIcon.TrayToolTipResolved;
            if (toolTip == null)
            {
                return;
            }

            // Hardcodet hosts custom content inside its own WPF ToolTip. Keep that
            // wrapper transparent so only the PowerTray-themed surface is visible.
            // Placement and open/close timing remain owned by Hardcodet and WPF.
            toolTip.Background = Brushes.Transparent;
            toolTip.BorderBrush = Brushes.Transparent;
            toolTip.BorderThickness = new Thickness(0);
            toolTip.Padding = new Thickness(0);
        }

        private void ApplyCustomTrayToolTipTheme()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(ApplyCustomTrayToolTipTheme);
                return;
            }

            if (_customTrayToolTipBorder == null
                || _customTrayToolTipText == null
                || Application.Current == null)
            {
                return;
            }

            if (Application.Current.TryFindResource("TooltipBackgroundBrush") is Brush background)
            {
                _customTrayToolTipBorder.Background = background;
            }

            if (Application.Current.TryFindResource("BorderBrushSoft") is Brush border)
            {
                _customTrayToolTipBorder.BorderBrush = border;
            }

            if (Application.Current.TryFindResource("TextBrush") is Brush foreground)
            {
                _customTrayToolTipText.Foreground = foreground;
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
            if (e.PropertyName is nameof(CheckTheme.LightTheme) or nameof(CheckTheme.ThemeMode))
            {
                ApplyCustomTrayToolTipTheme();
            }

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
        }

        private void DrawBatteryIcon()
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (disposedValue)
                {
                    return;
                }

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
