using LGSTrayHID;
using LGSTrayPrimitives;
using LGSTrayPrimitives.IPC;
using LGSTrayPrimitives.MessageStructs;
using LGSTrayUI;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

internal static class CongestedAlertCycleTests
{
    internal static async Task RunAsync()
    {
        // Isolate WPF Application, settings and authentication from other tests.
        ProcessStartInfo start = new(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(CongestedAlertCycleTests).Assembly.Location);
        start.ArgumentList.Add("--congested-alert-cycle-child");
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        Check(process.ExitCode == 0, "Congested alert regression failed: " + await stderr);
        Console.Write(await stdout);
        await DeviceEventBoundaryTests.RunAsync();
    }

    internal static async Task RunChildAsync()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                Application application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                Task scenarios = RunScenariosAsync();
                _ = scenarios.ContinueWith(_ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Send), TaskScheduler.Default);
                Dispatcher.Run();
                scenarios.GetAwaiter().GetResult();
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }

    private static async Task RunScenariosAsync()
    {
        _ = IpcSessionContext.CreateAndSetToken();
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerTray-alert-cycle-" + Guid.NewGuid().ToString("N")));
        PowerTrayConstants.UserDataDirectoryOverrideForTests = directory;
        try
        {
            UserSettingsWrapper settings = new()
            {
                Language = "en-US",
                DefaultThresholdPercent = 20,
                DefaultWindowsNotification = true,
                DefaultTrayBlink = true,
                QuietHoursEnabled = false,
                SuppressNotificationsWhenFullscreen = false,
            };
            await TestScenarioAsync(settings, "recovered-then-low", [Update(70), Update(5)], 2, true, 5);
            await TestScenarioAsync(settings, "charging-then-discharging", [Update(5, PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING), Update(5)], 2, true, 5);
            await TestScenarioAsync(settings, "full-then-discharging", [Update(5, PowerSupplyStatus.POWER_SUPPLY_STATUS_FULL), Update(5)], 2, true, 5);
            await TestScenarioAsync(settings, "offline-init-update", [Offline(), Init(), Update(5)], 2, true, 5);
            await TestScenarioAsync(settings, "offline-init-only", [Update(4), Offline(), Init()], 2, true, 4);
            await TestScenarioAsync(settings, "offline-update-only", [Offline(), Update(5)], 2, true, 5);
            await TestScenarioAsync(settings, "two-reconnect-cycles", [Offline(), Init(), Update(5), Offline(), Init(), Update(4)], 3, true, 4);
            await TestScenarioAsync(settings, "same-cycle", [Init(), Update(5), Init(), Update(4)], 1, true, 4);
            await TestScenarioAsync(settings, "reconnected-above-threshold", [Offline(), Init(), Update(70)], 1, true, 70);
            await TestScenarioAsync(settings, "reconnect-then-offline", [Offline(), Init(), Update(4), Offline()], 2, false, 4);
            await TestScenarioAsync(settings, "offline-only", [Offline()], 1, false, 5);
            Console.WriteLine("11 congested pipe -> WPF collection -> AlertManager scenarios passed.");
        }
        finally
        {
            PowerTrayConstants.UserDataDirectoryOverrideForTests = null;
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test directory escaped the temporary root.");
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task TestScenarioAsync(UserSettingsWrapper settings, string scenario,
        IPCMessage[] events, int expectedNotifications, bool expectedOnline, int expectedBattery)
    {
        ServiceCollection services = new();
        services.AddMessagePipe();
        services.AddSingleton<IDistributedSubscriber<IPCMessageType, IPCMessage>, InMemoryDistributedSubscriber<IPCMessageType, IPCMessage>>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        LocalizationService loc = new(settings);
        NotificationService notifications = new(loc);
        int targetNotifications = 0;
        int neighborNotifications = 0;
        notifications.NotificationRequested += (_, body) =>
        {
            if (body.Contains("Target", StringComparison.Ordinal)) targetNotifications++;
            if (body.Contains("Neighbor", StringComparison.Ordinal)) neighborNotifications++;
        };
        AlertStateService alertState = new();
        using AlertManager alerts = new(settings, alertState, notifications, new SystemStateService());
        // Advance periodic evaluation explicitly after draining instead of letting
        // a wall-clock timer conceal missing OFFLINE events or change counts mid-test.
        ((DispatcherTimer)typeof(AlertManager).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(alerts)!).Stop();
        using LogiDeviceCollection devices = new(settings, new LogiDeviceViewModelFactory(null!, settings, loc),
            provider.GetRequiredService<ISubscriber<IPCMessage>>(), Options.Create(new AppSettings()), alerts);
        alerts.SetDevices(devices.Devices);
        IPublisher<IPCMessage> bus = provider.GetRequiredService<IPublisher<IPCMessage>>();
        bus.Publish(Init());
        bus.Publish(Update(5));
        bus.Publish(new InitMessage("neighbor", "Neighbor", true, DeviceType.Mouse));
        bus.Publish(new UpdateMessage("neighbor", 5, PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING, 0, DateTimeOffset.UtcNow));
        await FlushDispatcherAsync();
        Check(targetNotifications == 1 && neighborNotifications == 1, scenario + ": seed must notify once for each low device.");

        string pipeName = "PowerTray.Tests.AlertCycle." + Guid.NewGuid().ToString("N");
        await using NamedPipePublisher<IPCMessageType, IPCMessage> publisher = new(pipeName);
        using CancellationTokenSource stop = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        // Hold the real transport full: one in-flight frame and 256 queued frames.
        for (int i = 0; i < 257; i++)
            await publisher.PublishAsync(IPCMessageType.HEARTBEAT, new HeartbeatMessage(), timeout.Token);
        TaskCompletionSource blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DeviceEventOutbox outbox = new(async (type, message, token) =>
        {
            IpcSessionContext.Sign(type, message);
            if (message.deviceId == "blocker") blocked.TrySetResult();
            await publisher.PublishAsync(type, message, token);
        }, stop.Token);
        try
        {
            outbox.Enqueue(IPCMessageType.UPDATE, new UpdateMessage("blocker", 0,
                PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING, 0, DateTimeOffset.UtcNow));
            await blocked.Task.WaitAsync(timeout.Token);
            for (int i = 0; i < 5000; i++) outbox.Enqueue(IPCMessageType.UPDATE, Update(5));
            foreach (IPCMessage message in events) outbox.Enqueue(TypeOf(message), message);
            outbox.Enqueue(IPCMessageType.UPDATE, new UpdateMessage("neighbor", 5,
                PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING, 0, DateTimeOffset.UtcNow));
            outbox.Enqueue(IPCMessageType.UPDATE, new UpdateMessage("sentinel", 0,
                PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING, 0, DateTimeOffset.UtcNow));

            IDistributedSubscriber<IPCMessageType, IPCMessage> subscriber = provider.GetRequiredService<IDistributedSubscriber<IPCMessageType, IPCMessage>>();
            List<IAsyncDisposable> subscriptions = [];
            try
            {
                foreach (IPCMessageType type in new[] { IPCMessageType.INIT, IPCMessageType.UPDATE, IPCMessageType.OFFLINE })
                {
                    subscriptions.Add(await subscriber.SubscribeAsync(type, message =>
                    {
                        if (!IpcSessionContext.Validate(type, message))
                        {
                            drained.TrySetException(new InvalidOperationException("Signed frame failed authentication."));
                            return;
                        }
                        bus.Publish(message);
                        if (message.deviceId == "sentinel") drained.TrySetResult();
                    }));
                }
                await using NamedPipeReceiver<IPCMessageType, IPCMessage> receiver = new(pipeName,
                    provider.GetRequiredService<IAsyncPublisher<IPCMessageType, IPCMessage>>());
                await drained.Task.WaitAsync(timeout.Token);
                await FlushDispatcherAsync();
                alerts.EvaluateAll(); // Reconnect via INIT need not have a new battery reading.
                Check(targetNotifications == expectedNotifications,
                    $"{scenario}: expected {expectedNotifications} low-battery notifications, got {targetNotifications}.");
                Check(neighborNotifications == 1, scenario + ": another device's cycle must not reset.");
                LogiDeviceViewModel target = devices.Devices.Single(device => device.DeviceId == "target");
                Check(target.IsOnline == expectedOnline && target.BatteryPercentage == expectedBattery,
                    scenario + ": final presence and last battery reading must survive congestion.");
                Check(alertState.IsBlinking("target") == (expectedOnline && expectedBattery <= 20),
                    scenario + ": final blink state must match the alert state.");
                alerts.EvaluateAll();
                Check(targetNotifications == expectedNotifications, scenario + ": repeated evaluation must not duplicate notifications.");
            }
            finally
            {
                foreach (IAsyncDisposable subscription in subscriptions) await subscription.DisposeAsync();
            }
        }
        finally
        {
            stop.Cancel();
            await outbox.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Console.WriteLine("Congested alert cycle: " + scenario + " passed.");
    }

    private static Task FlushDispatcherAsync() =>
        Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static InitMessage Init() => new("target", "Target", true, DeviceType.Mouse);
    private static DeviceOfflineMessage Offline() => new("target");
    private static UpdateMessage Update(int battery, PowerSupplyStatus status = PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING) =>
        new("target", battery, status, 0, DateTimeOffset.UtcNow);
    private static IPCMessageType TypeOf(IPCMessage message) => message switch
    {
        InitMessage => IPCMessageType.INIT,
        UpdateMessage => IPCMessageType.UPDATE,
        DeviceOfflineMessage => IPCMessageType.OFFLINE,
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
