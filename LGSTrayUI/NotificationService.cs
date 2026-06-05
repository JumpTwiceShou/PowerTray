using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Windows;

namespace LGSTrayUI;

public sealed class NotificationService
{
    private readonly LocalizationService _loc;

    public NotificationService(LocalizationService loc)
    {
        _loc = loc;
    }

    public event Action<string, string>? NotificationRequested;

    public void ShowLowBattery(LogiDeviceViewModel device)
    {
        string title = _loc["LowBatteryTitle"];
        string body = string.Format(_loc["LowBatteryBody"], device.DisplayName, device.BatteryPercentage);
        Show(title, body);
    }

    public void ShowTest(LogiDeviceViewModel device)
    {
        string title = _loc["TestNotificationTitle"];
        string body = string.Format(_loc["TestNotificationBody"], device.DisplayName);
        Show(title, body);
    }

    public void Show(string title, string body)
    {
        NotificationRequested?.Invoke(title, body);
    }

    public static void ShowBalloon(TaskbarIcon icon, string title, string body)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
            icon.ShowBalloonTip(title, body, BalloonIcon.Info)
        );
    }
}
