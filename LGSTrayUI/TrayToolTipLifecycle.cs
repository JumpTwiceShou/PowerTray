using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Windows.Controls;

namespace LGSTrayUI;

internal static class TrayToolTipLifecycle
{
    public static void CloseBeforeIconDisposal(TaskbarIcon taskbarIcon)
    {
        ArgumentNullException.ThrowIfNull(taskbarIcon);
        CloseAndDetach(
            taskbarIcon.TrayToolTipResolved,
            () => taskbarIcon.TrayToolTip = null
        );
    }

    internal static void CloseAndDetach(ToolTip? toolTip, Action detachFromIcon)
    {
        ArgumentNullException.ThrowIfNull(detachFromIcon);
        if (toolTip != null)
        {
            try
            {
                toolTip.IsOpen = false;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Disposal must continue even if WPF is already tearing down PopupRoot.
                // Clearing the content below still prevents a visible orphan surface.
            }

            toolTip.Content = null;
            toolTip.DataContext = null;
        }

        detachFromIcon();
    }
}
