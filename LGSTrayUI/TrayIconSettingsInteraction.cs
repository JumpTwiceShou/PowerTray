using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Windows.Controls;
using System.Windows.Data;

namespace LGSTrayUI;

internal static class TrayIconSettingsInteraction
{
    public static void Attach(TaskbarIcon taskbarIcon)
    {
        ArgumentNullException.ThrowIfNull(taskbarIcon);

        ContextMenu contextMenu = taskbarIcon.ContextMenu
            ?? throw new InvalidOperationException("The tray icon requires the shared context menu before settings interaction is attached.");

        BindingOperations.SetBinding(
            taskbarIcon,
            TaskbarIcon.DoubleClickCommandProperty,
            new Binding("DataContext.OpenSettingsCommand")
            {
                Mode = BindingMode.OneWay,
                Source = contextMenu,
            }
        );
    }

    public static void Detach(TaskbarIcon taskbarIcon)
    {
        ArgumentNullException.ThrowIfNull(taskbarIcon);
        BindingOperations.ClearBinding(taskbarIcon, TaskbarIcon.DoubleClickCommandProperty);
    }
}
