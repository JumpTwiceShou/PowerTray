namespace LGSTrayHID;

internal static class BatteryPollingLoop
{
    public static async Task RunAsync(
        Func<CancellationToken, Task> waitForNextPollAsync,
        Func<CancellationToken, Task> updateBatteryAsync,
        TimeSpan retryDelay,
        Action<Exception> recordError,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(waitForNextPollAsync);
        ArgumentNullException.ThrowIfNull(updateBatteryAsync);
        ArgumentNullException.ThrowIfNull(recordError);
        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await waitForNextPollAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await updateBatteryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A single unexpected HID/parser failure must not terminate this
                // device's only battery-poll task for the rest of the process.
                recordError(ex);
            }

            try
            {
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
