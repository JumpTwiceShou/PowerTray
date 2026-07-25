using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace LGSTrayUI;

internal sealed record RestartWaitTarget(int ProcessId, long StartTimeUtcTicks);

internal static class RestartWaitArguments
{
    public const string WaitForExitArgument = "--wait-for-exit";
    public const string WaitStartTimeArgument = "--wait-start-time-utc-ticks";

    public static bool TryExtract(
        IReadOnlyList<string> arguments,
        out RestartWaitTarget? target,
        out string[] remainingArguments
    )
    {
        ArgumentNullException.ThrowIfNull(arguments);

        int? processId = null;
        long? startTimeUtcTicks = null;
        List<string> remaining = [];

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument.Equals(WaitForExitArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (processId.HasValue || index + 1 >= arguments.Count ||
                    !int.TryParse(arguments[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedProcessId) ||
                    parsedProcessId <= 0)
                {
                    target = null;
                    remainingArguments = [];
                    return false;
                }

                processId = parsedProcessId;
                continue;
            }

            if (argument.Equals(WaitStartTimeArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (startTimeUtcTicks.HasValue || index + 1 >= arguments.Count ||
                    !long.TryParse(arguments[++index], NumberStyles.None, CultureInfo.InvariantCulture, out long parsedTicks) ||
                    parsedTicks <= 0)
                {
                    target = null;
                    remainingArguments = [];
                    return false;
                }

                startTimeUtcTicks = parsedTicks;
                continue;
            }

            remaining.Add(argument);
        }

        if (processId.HasValue != startTimeUtcTicks.HasValue)
        {
            target = null;
            remainingArguments = [];
            return false;
        }

        target = processId.HasValue
            ? new RestartWaitTarget(processId.Value, startTimeUtcTicks!.Value)
            : null;
        remainingArguments = remaining.ToArray();
        return true;
    }

    public static bool WaitForPriorInstance(RestartWaitTarget? target, TimeSpan timeout)
    {
        if (target == null)
        {
            return true;
        }

        if (target.ProcessId == Environment.ProcessId || timeout <= TimeSpan.Zero)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(target.ProcessId);
            long actualStartTimeUtcTicks;
            try
            {
                actualStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
            {
                return false;
            }

            if (actualStartTimeUtcTicks != target.StartTimeUtcTicks)
            {
                // The original process is already gone and Windows has reused the PID.
                return true;
            }

            int timeoutMilliseconds = (int)Math.Clamp(
                Math.Ceiling(timeout.TotalMilliseconds),
                1,
                int.MaxValue
            );
            return process.WaitForExit(timeoutMilliseconds);
        }
        catch (ArgumentException)
        {
            // No process currently owns the requested PID.
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return false;
        }
    }

    public static ProcessStartInfo CreateStartInfo(
        string executablePath,
        Process currentProcess,
        bool reopenSettings
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(currentProcess);

        string absoluteExecutablePath = Path.GetFullPath(executablePath);
        if (!Path.IsPathFullyQualified(absoluteExecutablePath) || !File.Exists(absoluteExecutablePath))
        {
            throw new FileNotFoundException("The PowerTray executable path is unavailable.", absoluteExecutablePath);
        }

        ProcessStartInfo startInfo = new(absoluteExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(absoluteExecutablePath) ?? AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add(WaitForExitArgument);
        startInfo.ArgumentList.Add(currentProcess.Id.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(WaitStartTimeArgument);
        startInfo.ArgumentList.Add(currentProcess.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        if (reopenSettings)
        {
            startInfo.ArgumentList.Add("--settings");
        }

        return startInfo;
    }
}
