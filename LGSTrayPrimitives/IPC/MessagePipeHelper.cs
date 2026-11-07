using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace LGSTrayPrimitives.IPC;

internal static class MessagePipeDiagnosticsPolicy
{
#if DEBUG
    public const bool CaptureStackTrace = true;
#else
    public const bool CaptureStackTrace = false;
#endif
}

public static class MessagePipeHelper
{
    public static void AddLGSMessagePipe(this IServiceCollection services, bool hostAsServer = false)
    {
        services.AddMessagePipe(options =>
        {
            options.EnableCaptureStackTrace = MessagePipeDiagnosticsPolicy.CaptureStackTrace;
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

    private static ServiceProvider BuildEndpoint<TKey, TMessage>(string pipeName, bool hostAsServer) where TKey : notnull
    {
        ServiceCollection services = new();
        services.AddMessagePipe(options =>
        {
            options.EnableCaptureStackTrace = MessagePipeDiagnosticsPolicy.CaptureStackTrace;
        });
        if (hostAsServer)
        {
            services.AddSingleton<IDistributedSubscriber<TKey, TMessage>, InMemoryDistributedSubscriber<TKey, TMessage>>();
            services.AddSingleton(provider => new NamedPipeReceiver<TKey, TMessage>(
                pipeName, provider.GetRequiredService<IAsyncPublisher<TKey, TMessage>>()));
        }
        else
        {
            services.AddSingleton<IDistributedPublisher<TKey, TMessage>>(_ => new NamedPipePublisher<TKey, TMessage>(pipeName));
        }

        ServiceProvider provider = services.BuildServiceProvider();
        if (hostAsServer) _ = provider.GetRequiredService<NamedPipeReceiver<TKey, TMessage>>();
        return provider;
    }

    private sealed class UiMessagePipeEndpoints : IDisposable
    {
        private readonly ServiceProvider _messageServer;
        private readonly ServiceProvider _requestClient;

        public IDistributedSubscriber<IPCMessageType, IPCMessage> MessageSubscriber { get; }
        public IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage> RequestPublisher { get; }

        public UiMessagePipeEndpoints()
        {
            _messageServer = BuildEndpoint<IPCMessageType, IPCMessage>(IpcNameScope.MessagePipeName("PowerTray.HidToUi"), hostAsServer: true);
            _requestClient = BuildEndpoint<IPCMessageRequestType, IPCRequestMessage>(IpcNameScope.MessagePipeName("PowerTray.UiToHid"), hostAsServer: false);
            MessageSubscriber = _messageServer.GetRequiredService<IDistributedSubscriber<IPCMessageType, IPCMessage>>();
            RequestPublisher = _requestClient.GetRequiredService<IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage>>();
        }

        public void Dispose()
        {
            try { _requestClient.Dispose(); }
            finally { _messageServer.Dispose(); }
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
            _messageClient = BuildEndpoint<IPCMessageType, IPCMessage>(IpcNameScope.MessagePipeName("PowerTray.HidToUi"), hostAsServer: false);
            _requestServer = BuildEndpoint<IPCMessageRequestType, IPCRequestMessage>(IpcNameScope.MessagePipeName("PowerTray.UiToHid"), hostAsServer: true);
            MessagePublisher = _messageClient.GetRequiredService<IDistributedPublisher<IPCMessageType, IPCMessage>>();
            RequestSubscriber = _requestServer.GetRequiredService<IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage>>();
        }

        public void Dispose()
        {
            try { _messageClient.Dispose(); }
            finally { _requestServer.Dispose(); }
        }
    }
}
