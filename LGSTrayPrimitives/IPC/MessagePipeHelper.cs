using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace LGSTrayPrimitives.IPC;

public static class MessagePipeHelper
{
    public static void AddLGSMessagePipe(this IServiceCollection services, bool hostAsServer = false)
    {
        services.AddMessagePipe(options =>
        {
            options.EnableCaptureStackTrace = true;
        });

        if (hostAsServer)
        {
            services.AddSingleton<UiMessagePipeEndpoints>();
            services.AddSingleton<IDistributedSubscriber<IPCMessageType, IPCMessage>>(provider =>
                provider.GetRequiredService<UiMessagePipeEndpoints>().MessageSubscriber);
            services.AddSingleton<IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage>>(provider =>
                provider.GetRequiredService<UiMessagePipeEndpoints>().RequestPublisher);
            return;
        }

        services.AddSingleton<HidMessagePipeEndpoints>();
        services.AddSingleton<IDistributedPublisher<IPCMessageType, IPCMessage>>(provider =>
            provider.GetRequiredService<HidMessagePipeEndpoints>().MessagePublisher);
        services.AddSingleton<IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage>>(provider =>
            provider.GetRequiredService<HidMessagePipeEndpoints>().RequestSubscriber);
    }

    private static ServiceProvider BuildEndpoint(string pipeName, bool hostAsServer)
    {
        ServiceCollection services = new();
        services.AddMessagePipe(options =>
        {
            options.EnableCaptureStackTrace = true;
        })
        .AddNamedPipeInterprocess(pipeName, config =>
        {
            config.HostAsServer = hostAsServer;
        });

        return services.BuildServiceProvider();
    }

    private sealed class UiMessagePipeEndpoints : IDisposable
    {
        private readonly ServiceProvider _messageServer;
        private readonly ServiceProvider _requestClient;

        public IDistributedSubscriber<IPCMessageType, IPCMessage> MessageSubscriber { get; }
        public IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage> RequestPublisher { get; }

        public UiMessagePipeEndpoints()
        {
            _messageServer = BuildEndpoint(IpcNameScope.MessagePipeName("PowerTray.HidToUi"), hostAsServer: true);
            _requestClient = BuildEndpoint(IpcNameScope.MessagePipeName("PowerTray.UiToHid"), hostAsServer: false);
            MessageSubscriber = _messageServer.GetRequiredService<IDistributedSubscriber<IPCMessageType, IPCMessage>>();
            RequestPublisher = _requestClient.GetRequiredService<IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage>>();
        }

        public void Dispose()
        {
            // MessagePipe 1.8.2 disposes its CancellationTokenSource before its async-void
            // named-pipe receive loop has necessarily observed cancellation. Process lifetime
            // owns these endpoints, so let process teardown release the pipe handles safely.
        }
    }

    private sealed class HidMessagePipeEndpoints : IDisposable
    {
        private readonly ServiceProvider _messageClient;
        private readonly ServiceProvider _requestServer;

        public IDistributedPublisher<IPCMessageType, IPCMessage> MessagePublisher { get; }
        public IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage> RequestSubscriber { get; }

        public HidMessagePipeEndpoints()
        {
            _messageClient = BuildEndpoint(IpcNameScope.MessagePipeName("PowerTray.HidToUi"), hostAsServer: false);
            _requestServer = BuildEndpoint(IpcNameScope.MessagePipeName("PowerTray.UiToHid"), hostAsServer: true);
            MessagePublisher = _messageClient.GetRequiredService<IDistributedPublisher<IPCMessageType, IPCMessage>>();
            RequestSubscriber = _requestServer.GetRequiredService<IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage>>();
        }

        public void Dispose()
        {
            // See UiMessagePipeEndpoints.Dispose. The helper process owns these endpoints
            // for its entire lifetime, so explicit worker disposal adds shutdown risk only.
        }
    }
}
