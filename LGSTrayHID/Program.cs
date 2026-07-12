using LGSTrayPrimitives;
using LGSTrayPrimitives.IPC;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using Tommy.Extensions.Configuration;

namespace LGSTrayHID;

internal static class GlobalSettings
{
    public static NativeDeviceManagerSettings settings = new();
}

internal class Program
{
    private static async Task Main(string[] args)
    {
        string? ipcToken = Environment.GetEnvironmentVariable(IpcSessionContext.EnvironmentVariableName);
#if DEBUG
        if (string.IsNullOrWhiteSpace(ipcToken))
        {
            ipcToken = IpcSessionContext.CreateAndSetToken();
        }
        else
        {
            IpcSessionContext.SetToken(ipcToken);
        }
#else
        if (string.IsNullOrWhiteSpace(ipcToken))
        {
            return;
        }
        IpcSessionContext.SetToken(ipcToken);
#endif
        Environment.SetEnvironmentVariable(IpcSessionContext.EnvironmentVariableName, null);

        bool hasParent = int.TryParse(args.ElementAtOrDefault(0), out int parentPid);
#if !DEBUG
        if (!hasParent)
        {
            return;
        }
#endif

        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddTomlFile("appsettings.toml");
        GlobalSettings.settings = builder.Configuration.GetSection("Native")
            .Get<NativeDeviceManagerSettings>() ?? GlobalSettings.settings;

        builder.Services.AddLGSMessagePipe();
        builder.Services.AddHostedService<HidppManagerService>();

        using IHost host = builder.Build();
        Task? parentWatcher = hasParent ? WatchParentAsync(parentPid, host) : null;

        try
        {
            await host.RunAsync();
        }
        finally
        {
            if (parentWatcher != null)
            {
                try
                {
                    await parentWatcher;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PowerTrayHID parent watcher failed: {ex}");
                }
            }
        }
    }

    private static async Task WatchParentAsync(int parentPid, IHost host)
    {
        try
        {
            using Process parent = Process.GetProcessById(parentPid);
            await parent.WaitForExitAsync();
        }
        catch (ArgumentException)
        {
            // Parent disappeared before the watcher attached. Stop the helper immediately.
        }
        catch (InvalidOperationException)
        {
            // Treat an unavailable parent process as an exit signal.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access/query failures must not leave an orphan helper.
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        try
        {
            await host.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("PowerTrayHID host stop timed out after parent exit.");
        }
    }
}
