using System.Diagnostics;

namespace LGSTrayHID;

internal sealed class RediscoveryScheduler : IDisposable
{
    private static readonly TimeSpan[] DefaultRetryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(1000),
        TimeSpan.FromMilliseconds(2500),
        TimeSpan.FromMilliseconds(5000),
    ];
    internal static IReadOnlyList<TimeSpan> DefaultRetrySchedule => DefaultRetryDelays;

    private readonly object _sync = new();
    private readonly Func<string, bool, CancellationToken, Task<bool>> _runPass;
    private readonly CancellationToken _lifetimeToken;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    private CancellationTokenSource? _arrivalBurstCts;
    private CancellationTokenSource? _delayedRequestCts;
    private bool _disposed;

    public RediscoveryScheduler(
        Func<string, bool, CancellationToken, Task<bool>> runPass,
        CancellationToken lifetimeToken,
        IReadOnlyList<TimeSpan>? retryDelays = null
    )
    {
        _runPass = runPass;
        _lifetimeToken = lifetimeToken;
        _retryDelays = retryDelays ?? DefaultRetryDelays;

        if (_retryDelays.Count == 0 ||
            _retryDelays.Any(delay => delay < TimeSpan.Zero) ||
            !_retryDelays.SequenceEqual(_retryDelays.OrderBy(delay => delay)))
        {
            throw new ArgumentException("Rediscovery retry delays must be non-negative and ordered.", nameof(retryDelays));
        }
    }

    public Task RunNowAsync(
        string reason,
        CancellationToken cancellationToken,
        bool retryIncompleteOnly = false
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RunSinglePassAsync(reason, retryIncompleteOnly, cancellationToken);
    }

    public Task RunBoundedAsync(string reason, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RunRetrySequenceAsync(
            reason,
            cancellationToken,
            retryIncompleteOnly: false,
            stopWhenComplete: true,
            continueAfterFailure: false
        );
    }

    public Task RunIncompleteRetryAsync(string reason, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RunRetrySequenceAsync(
            reason,
            cancellationToken,
            retryIncompleteOnly: true,
            stopWhenComplete: true,
            continueAfterFailure: false
        );
    }

    public Task RestartArrivalBurst(string reason)
    {
        CancellationTokenSource cts;
        CancellationTokenSource? previous;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cts = CreateLinkedTokenSource();
            previous = _arrivalBurstCts;
            _arrivalBurstCts = cts;
        }

        previous?.Cancel();
        return RunTrackedAsync(
            cts,
            () => RunRetrySequenceAsync(
                reason,
                cts.Token,
                retryIncompleteOnly: false,
                stopWhenComplete: false,
                continueAfterFailure: true
            ),
            () => ClearIfCurrent(ref _arrivalBurstCts, cts)
        );
    }

    public Task ScheduleLatest(
        TimeSpan delay,
        string reason,
        bool retryIncompleteOnly = false
    )
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        CancellationTokenSource cts;
        CancellationTokenSource? previous;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cts = CreateLinkedTokenSource();
            previous = _delayedRequestCts;
            _delayedRequestCts = cts;
        }

        previous?.Cancel();
        return RunTrackedAsync(
            cts,
            async () =>
            {
                await Task.Delay(delay, cts.Token);
                await RunSinglePassAsync(reason, retryIncompleteOnly, cts.Token);
            },
            () => ClearIfCurrent(ref _delayedRequestCts, cts)
        );
    }

    public void Dispose()
    {
        CancellationTokenSource? arrival;
        CancellationTokenSource? delayed;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            arrival = _arrivalBurstCts;
            delayed = _delayedRequestCts;
            _arrivalBurstCts = null;
            _delayedRequestCts = null;
        }

        arrival?.Cancel();
        delayed?.Cancel();
    }

    private CancellationTokenSource CreateLinkedTokenSource()
    {
        return CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
    }

    private async Task RunSinglePassAsync(
        string reason,
        bool retryIncompleteOnly,
        CancellationToken cancellationToken
    )
    {
        _ = await _runPass(reason, retryIncompleteOnly, cancellationToken);
    }

    private async Task RunRetrySequenceAsync(
        string reason,
        CancellationToken cancellationToken,
        bool retryIncompleteOnly,
        bool stopWhenComplete,
        bool continueAfterFailure
    )
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        for (int attempt = 0; attempt < _retryDelays.Count; attempt++)
        {
            TimeSpan delay = _retryDelays[attempt];
            TimeSpan wait = delay - elapsed.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            try
            {
                bool hasIncompleteSessions = await _runPass(
                    reason,
                    retryIncompleteOnly || attempt > 0,
                    cancellationToken
                );
                if (stopWhenComplete && !hasIncompleteSessions)
                {
                    return;
                }
            }
            catch when (continueAfterFailure && !cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task RunTrackedAsync(
        CancellationTokenSource cts,
        Func<Task> action,
        Action clearCurrent
    )
    {
        try
        {
            await Task.Run(action, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        finally
        {
            clearCurrent();
            cts.Dispose();
        }
    }

    private void ClearIfCurrent(ref CancellationTokenSource? field, CancellationTokenSource cts)
    {
        lock (_sync)
        {
            if (ReferenceEquals(field, cts))
            {
                field = null;
            }
        }
    }
}
