using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Windows;
using System.Windows.Threading;

namespace LGSTrayUI;

public class MainTaskBarIcon : TaskbarIcon
{
    public MainTaskBarIcon() : base()
    {
        ContextMenu = (System.Windows.Controls.ContextMenu)Application.Current.FindResource("SysTrayMenu");
        BatteryIconDrawing.DrawUnknown(this);
    }
}

public class MainTaskbarIconWrapper : IDisposable
{
    private readonly AlertStateService _alertState;
    private readonly NotificationService _notificationService;
    private readonly DispatcherTimer _blinkTimer;
    private TaskbarIcon? _taskbarIcon;
    private bool _blinkVisible = true;
    private bool disposedValue;

    public MainTaskbarIconWrapper(AlertStateService alertState, NotificationService notificationService)
    {
        _alertState = alertState;
        _notificationService = notificationService;
        _alertState.Changed += OnAlertStateChanged;
        _notificationService.NotificationRequested += OnNotificationRequested;
        LogiDeviceIcon.RefCountChanged += OnDeviceIconRefCountChanged;
        CheckTheme.StaticPropertyChanged += OnThemeChanged;
        _blinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkVisible = !_blinkVisible;
            DrawMainIcon();
        };
        UpdateMainIconVisibility(LogiDeviceIcon.RefCount);
        OnAlertStateChanged();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposedValue)
        {
            return;
        }

        if (disposing)
        {
            _blinkTimer.Stop();
            _alertState.Changed -= OnAlertStateChanged;
            _notificationService.NotificationRequested -= OnNotificationRequested;
            LogiDeviceIcon.RefCountChanged -= OnDeviceIconRefCountChanged;
            CheckTheme.StaticPropertyChanged -= OnThemeChanged;
            _taskbarIcon?.Dispose();
            _taskbarIcon = null;
        }

        disposedValue = true;
    }

    private void OnDeviceIconRefCountChanged(int refCount)
    {
        Application.Current.Dispatcher.BeginInvoke(() => UpdateMainIconVisibility(refCount));
    }

    private void UpdateMainIconVisibility(int deviceIconCount)
    {
        if (disposedValue)
        {
            return;
        }

        if (deviceIconCount > 0)
        {
            _taskbarIcon?.Dispose();
            _taskbarIcon = null;
            return;
        }

        _taskbarIcon ??= new MainTaskBarIcon();
        DrawMainIcon();
    }

    private void OnAlertStateChanged()
    {
        if (_alertState.HasAnyBlinking && !_blinkTimer.IsEnabled)
        {
            _blinkVisible = true;
            _blinkTimer.Start();
        }
        else if (!_alertState.HasAnyBlinking && _blinkTimer.IsEnabled)
        {
            _blinkTimer.Stop();
            _blinkVisible = true;
        }

        DrawMainIcon();
    }

    private void OnThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CheckTheme.TaskbarLightTheme) or nameof(CheckTheme.TaskbarThemeSuffix))
        {
            Application.Current.Dispatcher.BeginInvoke(DrawMainIcon);
        }
    }

    private void DrawMainIcon()
    {
        if (_taskbarIcon == null)
        {
            return;
        }

        if (_alertState.HasAnyBlinking && !_blinkVisible)
        {
            BatteryIconDrawing.DrawAlert(_taskbarIcon, new() { BatteryPercentage = 0 });
            return;
        }

        BatteryIconDrawing.DrawUnknown(_taskbarIcon);
    }

    private void OnNotificationRequested(string title, string body)
    {
        if (_taskbarIcon != null)
        {
            NotificationService.ShowBalloon(_taskbarIcon, title, body);
            return;
        }

        if (!LogiDeviceIcon.ShowBalloonOnFirstIcon(title, body))
        {
            UpdateMainIconVisibility(0);
            if (_taskbarIcon != null)
            {
                NotificationService.ShowBalloon(_taskbarIcon, title, body);
            }
        }
    }
}
