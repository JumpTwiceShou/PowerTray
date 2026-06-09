using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.Hosting;

namespace LGSTrayHID
{
    public class HidppManagerService : IHostedService
    {
        private readonly IDistributedPublisher<IPCMessageType, IPCMessage> _publisher;
        private readonly CancellationTokenSource _rediscoverSignalCts = new();
        private EventWaitHandle? _rediscoverSignal;
        private Task? _rediscoverSignalTask;

        public HidppManagerService(
            IDistributedPublisher<IPCMessageType, IPCMessage> publisher
        )
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
            _rediscoverSignal = LGSTrayPrimitives.IPC.NativeRediscoverSignal.CreateListener();
            _rediscoverSignalTask = Task.Run(
                () => RunRediscoverSignalLoopAsync(_rediscoverSignalCts.Token),
                CancellationToken.None
            );
            HidppManagerContext.Instance.Start(cancellationToken);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _rediscoverSignalCts.Cancel();

            if (_rediscoverSignalTask != null)
            {
                try
                {
                    await _rediscoverSignalTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }

                _rediscoverSignalTask = null;
            }

            _rediscoverSignal?.Dispose();
            _rediscoverSignal = null;

            HidppManagerContext.Instance.Stop();
        }

        private async Task RunRediscoverSignalLoopAsync(CancellationToken cancellationToken)
        {
            EventWaitHandle? rediscoverSignal = _rediscoverSignal;
            if (rediscoverSignal == null)
            {
                return;
            }

            WaitHandle[] handles = [rediscoverSignal, cancellationToken.WaitHandle];
            while (!cancellationToken.IsCancellationRequested)
            {
                int signaled = WaitHandle.WaitAny(handles, TimeSpan.FromSeconds(1));
                if (signaled != 0)
                {
                    continue;
                }

                HidppManagerContext.Instance.RediscoverDevices();
                await PublishDiagnosticsSnapshotAsync();
            }
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
