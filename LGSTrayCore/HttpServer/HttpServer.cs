using EmbedIO;
using EmbedIO.Net;
using EmbedIO.WebApi;
using LGSTrayPrimitives;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace LGSTrayCore.HttpServer
{
    public class HttpServer : IHostedService, IDisposable
    {
        #region IDisposable
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _server.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~WebServer()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion

        private readonly AppSettings _appSettings;
        private readonly HttpControllerFactory _httpControllerFactory;

        private CancellationTokenSource _serverCts = null!;
        private WebServer _server = null!;
        private Task? _serverTask;

        public HttpServer(IOptions<AppSettings> appSettings, HttpControllerFactory httpControllerFactory)
        {
            _appSettings = appSettings.Value;
            _httpControllerFactory = httpControllerFactory;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _serverCts = new();
            _server = CreateServer(_appSettings, _httpControllerFactory);
            _serverTask = _server.RunAsync(_serverCts.Token);
            _ = _serverTask.ContinueWith(
                task => Debug.WriteLine($"PowerTray HTTP server stopped unexpectedly: {task.Exception}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _serverCts.Cancel();

            if (_serverTask == null)
            {
                return;
            }

            try
            {
                await _serverTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static WebServer CreateServer(AppSettings appSettings, HttpControllerFactory httpControllerFactory)
        {
            EndPointManager.UseIpv6 = appSettings.HTTPServer.UseIpv6;

            var server = new WebServer(o => o.WithUrlPrefix(appSettings.HTTPServer.UrlPrefix))
                .WithWebApi("/", m => m.WithController(httpControllerFactory.CreateController));

            return server;
        }
    }
}
