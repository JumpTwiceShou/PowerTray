using System;
using System.Threading;

namespace LGSTrayPrimitives.IPC;

public static class NativeRediscoverSignal
{
    private const string EventName = @"Local\PowerTray.NativeBattery.Rediscover";

    public static EventWaitHandle CreateListener()
    {
        return new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
    }

    public static bool TrySignal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(EventName);
            return handle.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
