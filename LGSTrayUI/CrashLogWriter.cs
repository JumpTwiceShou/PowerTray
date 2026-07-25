using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LGSTrayUI;

internal static class CrashLogWriter
{
    public static bool TryWrite(Exception exception, string? directory = null, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            string targetDirectory = string.IsNullOrWhiteSpace(directory)
                ? PowerTrayConstants.UserDataDirectory
                : directory;
            Directory.CreateDirectory(targetDirectory);

            DateTimeOffset occurredAt = timestamp ?? DateTimeOffset.Now;
            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "crashlog_{0:yyyyMMdd-HHmmss-fff}_{1}_{2:N}.log",
                occurredAt,
                Environment.ProcessId,
                Guid.NewGuid()
            );
            string path = Path.Combine(targetDirectory, fileName);
            File.WriteAllText(path, exception.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch
        {
            // Crash reporting is best effort. Never replace the original fatal error
            // with a second exception caused by an unavailable or read-only path.
            return false;
        }
    }
}
