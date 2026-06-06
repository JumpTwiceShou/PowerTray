using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.Hosting;

namespace LGSTrayHID
{
    public class HidppManagerService : IHostedService
    {
        private readonly IDistributedPublisher<IPCMessageType, IPCMessage> _publisher;

        public HidppManagerService(IDistributedPublisher<IPCMessageType, IPCMessage> publisher)
        {
            _publisher = publisher;

            HidppManagerContext.Instance.HidppDeviceEvent += async (type, message) =>
            {
#if DEBUG
                if (message is InitMessage initMessage)
                {
                    Console.WriteLine(initMessage.deviceName);
                }
#endif

                await _publisher.PublishAsync(type, message);
                if (type is IPCMessageType.INIT or IPCMessageType.UPDATE or IPCMessageType.OFFLINE)
                {
                    await PublishDiagnosticsSnapshotAsync();
                }
            };
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            HidppManagerContext.Instance.Start(cancellationToken);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            HidppManagerContext.Instance.Stop();
            return Task.CompletedTask;
        }

        private async Task PublishDiagnosticsSnapshotAsync()
        {
            await _publisher.PublishAsync(
                IPCMessageType.NATIVE_DIAGNOSTICS_RESPONSE,
                new NativeDiagnosticsResponseMessage(
                    NativeDiagnosticsResponseMessage.LatestSnapshotRequestId,
                    NativeDiagnosticsStore.GetJson(),
                    NativeDiagnosticsStore.GetSummary()
                )
            );
        }
    }
}
