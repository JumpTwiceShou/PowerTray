namespace LGSTrayCore.HttpServer;

public sealed class HttpServerStatus
{
    private readonly object _sync = new();
    private HttpServerStatusSnapshot _snapshot = new("stopped", null, null, 0);

    public HttpServerStatusSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public void MarkRunning()
    {
        Update(current => current with
        {
            State = "running",
            StartedAt = DateTimeOffset.UtcNow,
            LastError = null,
        });
    }

    public void MarkRestarting(string error)
    {
        Update(current => current with
        {
            State = "restarting",
            LastError = error,
            RestartCount = current.RestartCount + 1,
        });
    }

    public void MarkStopped()
    {
        Update(current => current with { State = "stopped" });
    }

    private void Update(Func<HttpServerStatusSnapshot, HttpServerStatusSnapshot> update)
    {
        lock (_sync)
        {
            _snapshot = update(_snapshot);
        }
    }
}

public sealed record HttpServerStatusSnapshot(
    string State,
    DateTimeOffset? StartedAt,
    string? LastError,
    int RestartCount
);
