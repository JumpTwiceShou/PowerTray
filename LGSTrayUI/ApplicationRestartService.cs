using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace LGSTrayUI;

public sealed class ApplicationRestartService
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly LocalizationService _loc;

    public ApplicationRestartService(
        IHostApplicationLifetime applicationLifetime,
        LocalizationService loc
    )
    {
        _applicationLifetime = applicationLifetime;
        _loc = loc;
    }

    public bool TryRestart(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        try
        {
            string executablePath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, PowerTrayConstants.MainExecutable);
            using Process currentProcess = Process.GetCurrentProcess();
            ProcessStartInfo startInfo = RestartWaitArguments.CreateStartInfo(
                executablePath,
                currentProcess,
                reopenSettings: true
            );
            using Process replacement = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The replacement PowerTray process did not start.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _ = ThemedMessageBox.ShowOptions(
                owner,
                _loc["RestartFailedBody"],
                _loc["RestartFailedTitle"],
                [new ThemedDialogOption(_loc["OK"], "ok", IsDefault: true, IsCancel: true)],
                ex.Message
            );
            return false;
        }

        // The replacement process waits for this exact PID and start time before it
        // attempts the single-instance mutex or initializes tray, IPC, or HID state.
        // Stop through the host so every icon, WPF Popup, named pipe, and helper process
        // follows the existing normal disposal path.
        _applicationLifetime.StopApplication();
        return true;
    }
}
