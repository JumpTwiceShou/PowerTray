using LGSTrayPrimitives;

namespace LGSTrayHID;

internal readonly record struct AdaptiveBatteryPollingSettings(
    int FallbackPeriodSeconds,
    int DischargingPeriodSeconds,
    int ChargingPeriodSeconds,
    int LowBatteryPeriodSeconds,
    int LowBatteryThresholdPercent
);

internal readonly record struct AdaptiveBatteryPollingState(
    bool HasBatteryState,
    double BatteryPercentage,
    PowerSupplyStatus Status
);

internal static class AdaptiveBatteryPollingPolicy
{
    private const int MinimumPollPeriodSeconds = 30;
    private const int MaximumPollPeriodSeconds = 86400;

    public static TimeSpan GetPollInterval(
        AdaptiveBatteryPollingSettings settings,
        AdaptiveBatteryPollingState state
    )
    {
        int fallbackSeconds = ClampPeriod(settings.FallbackPeriodSeconds);
        if (!state.HasBatteryState)
        {
            return TimeSpan.FromSeconds(fallbackSeconds);
        }

        int threshold = Math.Clamp(settings.LowBatteryThresholdPercent, 1, 100);
        int selectedSeconds;
        if (double.IsFinite(state.BatteryPercentage) &&
            state.BatteryPercentage >= 0 &&
            state.BatteryPercentage <= threshold)
        {
            selectedSeconds = ClampPeriod(settings.LowBatteryPeriodSeconds);
        }
        else
        {
            selectedSeconds = state.Status switch
            {
                PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING =>
                    ClampPeriod(settings.ChargingPeriodSeconds),
                PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING or
                PowerSupplyStatus.POWER_SUPPLY_STATUS_NOT_CHARGING =>
                    ClampPeriod(settings.DischargingPeriodSeconds),
                _ => fallbackSeconds,
            };
        }

        return TimeSpan.FromSeconds(Math.Min(fallbackSeconds, selectedSeconds));
    }

    private static int ClampPeriod(int periodSeconds) =>
        Math.Clamp(periodSeconds, MinimumPollPeriodSeconds, MaximumPollPeriodSeconds);
}

internal sealed class AdaptiveBatteryPollSchedule
{
    private readonly object _sync = new();
    private CancellationTokenSource? _waitCancellation;
    private long _revision;

    public async Task WaitUntilDueAsync(
        Func<DateTimeOffset> getLastActivity,
        Func<TimeSpan> getPollInterval,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(getLastActivity);
        ArgumentNullException.ThrowIfNull(getPollInterval);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long revision;
            lock (_sync)
            {
                revision = _revision;
            }

            DateTimeOffset dueAt = getLastActivity().Add(getPollInterval());
            TimeSpan delay = dueAt - DateTimeOffset.Now;
            if (delay <= TimeSpan.Zero)
            {
                return;
            }

            using CancellationTokenSource waitCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bool scheduleChanged;
            lock (_sync)
            {
                scheduleChanged = revision != _revision;
                if (!scheduleChanged)
                {
                    _waitCancellation = waitCancellation;
                }
            }
            if (scheduleChanged)
            {
                continue;
            }

            try
            {
                await Task.Delay(delay, waitCancellation.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A fresh battery update changed last activity and possibly the
                // effective interval. Recalculate both before polling.
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_waitCancellation, waitCancellation))
                    {
                        _waitCancellation = null;
                    }
                }
            }
        }
    }

    public void Reschedule()
    {
        lock (_sync)
        {
            _revision++;
            _waitCancellation?.Cancel();
        }
    }
}
