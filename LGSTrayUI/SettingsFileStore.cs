using System;
using System.IO;
using System.Text;
using System.Threading;

namespace LGSTrayUI;

internal static class SettingsFileStore
{
    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAtomic(string settingsPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentNullException.ThrowIfNull(content);

        lock (Sync)
        {
            string? directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = $"{settingsPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            string backupPath = settingsPath + ".bak";
            try
            {
                using (FileStream stream = new(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough
                ))
                using (StreamWriter writer = new(stream, Utf8WithoutBom))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(settingsPath))
                {
                    File.Replace(tempPath, settingsPath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, settingsPath);
                }
            }
            finally
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Preserve the original persistence exception. A uniquely named
                    // orphaned temporary file is harmless and can be removed later.
                }
            }
        }
    }
}

internal sealed class SettingsSaveCoordinator
{
    private readonly object _saveGate = new();
    private long _requestedRevision;

    internal long RequestedRevision => Volatile.Read(ref _requestedRevision);

    public void Save(Func<string> snapshotFactory, Action<string> persist)
    {
        ArgumentNullException.ThrowIfNull(snapshotFactory);
        ArgumentNullException.ThrowIfNull(persist);

        Interlocked.Increment(ref _requestedRevision);
        lock (_saveGate)
        {
            while (true)
            {
                long revision = Volatile.Read(ref _requestedRevision);
                string content = snapshotFactory();
                if (revision != Volatile.Read(ref _requestedRevision))
                {
                    continue;
                }

                persist(content);
                if (revision == Volatile.Read(ref _requestedRevision))
                {
                    return;
                }
            }
        }
    }
}
