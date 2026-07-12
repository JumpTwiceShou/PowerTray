using EmbedIO;
using EmbedIO.Net;
using EmbedIO.WebApi;
using LGSTrayPrimitives;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace LGSTrayCore.HttpServer;

public sealed class HttpServer : IHostedService, IDisposable, IAsyncDisposable
{
    private readonly AppSettings _appSettings;
    private readonly HttpControllerFactory _httpControllerFactory;
    private readonly HttpServerStatus _status;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _serverSync = new();

    private WebServer? _server;
    private Task? _supervisorTask;
    private bool _disposed;

    public HttpServer(
        IOptions<AppSettings> appSettings,
        HttpControllerFactory httpControllerFactory,
        HttpServerStatus status
    )
    {
        _appSettings = appSettings.Value;
        _httpControllerFactory = httpControllerFactory;
        _status = status;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _supervisorTask = Task.Run(() => RunSupervisorAsync(_lifetimeCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _lifetimeCts.Cancel();
        WebServer? server;
        lock (_serverSync)
        {
            server = _server;
        }
        server?.Dispose();

        if (_supervisorTask != null)
        {
            try
            {
                await _supervisorTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _supervisorTask = null;
            }
        }
        _status.MarkStopped();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts.Cancel();
        lock (_serverSync)
        {
            _server?.Dispose();
            _server = null;
        }
        _lifetimeCts.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        try
        {
            await StopAsync(timeout.Token);
        }
        finally
        {
            Dispose();
        }
    }

    private async Task RunSupervisorAsync(CancellationToken cancellationToken)
    {
        int consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            WebServer? server = null;
            try
            {
                server = CreateServer(_appSettings, _httpControllerFactory);
                lock (_serverSync)
                {
                    _server = server;
                }
                _status.MarkRunning();
                DateTimeOffset startedAt = DateTimeOffset.UtcNow;
                await server.RunAsync(cancellationToken);
                if (DateTimeOffset.UtcNow - startedAt > TimeSpan.FromSeconds(30))
                {
                    consecutiveFailures = 0;
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    throw new InvalidOperationException("PowerTray HTTP server stopped unexpectedly.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                string error = $"{ex.GetType().Name}: {ex.Message}";
                Debug.WriteLine($"PowerTray HTTP server failed: {error}");
                _status.MarkRestarting(error);

                int delaySeconds = Math.Min(30, 1 << Math.Min(consecutiveFailures, 5));
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                lock (_serverSync)
                {
                    if (ReferenceEquals(_server, server))
                    {
                        _server = null;
                    }
                }
                server?.Dispose();
            }
        }

        _status.MarkStopped();
    }

    private static WebServer CreateServer(AppSettings appSettings, HttpControllerFactory httpControllerFactory)
    {
        EndPointManager.UseIpv6 = appSettings.HTTPServer.UseIpv6;
        return new WebServer(options => options.WithUrlPrefix(appSettings.HTTPServer.UrlPrefix))
            .WithWebApi("/", module => module.WithController(httpControllerFactory.CreateController));
    }
}
