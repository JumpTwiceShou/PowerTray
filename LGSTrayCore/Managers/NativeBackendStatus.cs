using LGSTrayPrimitives.MessageStructs;

namespace LGSTrayCore.Managers;

public sealed class NativeBackendStatus
{
    private readonly object _sync = new();
    private NativeBackendStatusSnapshot _snapshot = new(
        "stopped",
        null,
        null,
        null,
        null,
        null,
        0
    );

    public NativeBackendStatusSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public void MarkStarting(int restartCount)
    {
        Update(current => current with
        {
            State = "starting",
            LastError = null,
            RestartCount = restartCount,
        });
    }

    public void MarkHeartbeat(HeartbeatMessage heartbeat)
    {
        Update(current => current with
        {
            State = heartbeat.state,
            HelperProcessId = heartbeat.helperProcessId,
            HelperStartedAt = heartbeat.startedAt,
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            LastSuccessfulCommandAt = heartbeat.lastSuccessfulCommandAt == DateTimeOffset.MinValue
                ? null
                : heartbeat.lastSuccessfulCommandAt,
            LastError = heartbeat.lastError,
        });
    }

    public void MarkStopped()
    {
        Update(current => current with
        {
            State = "stopped",
            HelperProcessId = null,
        });
    }

    public void MarkFault(string error, int restartCount)
    {
        Update(current => current with
        {
            State = "faulted",
            LastError = error,
            RestartCount = restartCount,
        });
    }

    private void Update(Func<NativeBackendStatusSnapshot, NativeBackendStatusSnapshot> update)
    {
        lock (_sync)
        {
            _snapshot = update(_snapshot);
        }
    }
}

public sealed record NativeBackendStatusSnapshot(
    string State,
    int? HelperProcessId,
    DateTimeOffset? HelperStartedAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastSuccessfulCommandAt,
    string? LastError,
    int RestartCount
);
