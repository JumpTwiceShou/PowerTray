using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LGSTrayHID;

internal static class CenturionTraceWriter
{
    internal const string TracePathEnvironmentVariable = "POWERTRAY_CENTURION_TRACE_PATH";
    private const long MaximumTraceBytes = 8 * 1024 * 1024;

    private static readonly object Sync = new();
    private static readonly Stopwatch Elapsed = Stopwatch.StartNew();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static StreamWriter? _writer;
    private static bool _initializationAttempted;
    private static long _bytesWritten;
    private static bool _limitRecorded;

    internal static void Record(
        string direction,
        ushort productId,
        string endpointPathHash,
        byte[] frame
    )
    {
        lock (Sync)
        {
            try
            {
                if (!TryInitialize() || _writer == null || _bytesWritten >= MaximumTraceBytes)
                {
                    return;
                }

                byte? reportId = null;
                byte? deviceAddress = null;
                string? payload = null;
                if (CenturionFrameCodec.TryExtractPayload(frame, out byte decodedReportId, out byte? decodedAddress, out byte[] decodedPayload))
                {
                    reportId = decodedReportId;
                    deviceAddress = decodedAddress;
                    payload = Convert.ToHexString(decodedPayload);
                }

                CenturionTraceRecord record = new()
                {
                    Utc = DateTimeOffset.UtcNow,
                    ElapsedMilliseconds = Elapsed.Elapsed.TotalMilliseconds,
                    Direction = direction,
                    ProductId = $"0x{productId:X4}",
                    EndpointPathHash = endpointPathHash,
                    Frame = Convert.ToHexString(frame),
                    ReportId = reportId.HasValue ? $"0x{reportId.Value:X2}" : null,
                    DeviceAddress = deviceAddress.HasValue ? $"0x{deviceAddress.Value:X2}" : null,
                    Payload = payload,
                };
                WriteLine(JsonSerializer.Serialize(record, JsonOptions));

                if (_bytesWritten >= MaximumTraceBytes && !_limitRecorded)
                {
                    _limitRecorded = true;
                    WriteLine(JsonSerializer.Serialize(new
                    {
                        utc = DateTimeOffset.UtcNow,
                        elapsedMilliseconds = Elapsed.Elapsed.TotalMilliseconds,
                        kind = "traceLimitReached",
                        maximumTraceBytes = MaximumTraceBytes,
                    }, JsonOptions));
                }
            }
            catch
            {
                CloseWriter();
            }
        }
    }

    private static bool TryInitialize()
    {
        if (_initializationAttempted)
        {
            return _writer != null;
        }

        _initializationAttempted = true;
        string? tracePath = Environment.GetEnvironmentVariable(TracePathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(tracePath))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(tracePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        FileStream stream = new(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete
        );
        _writer = new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        WriteLine(JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow,
            elapsedMilliseconds = Elapsed.Elapsed.TotalMilliseconds,
            kind = "traceStarted",
            processId = Environment.ProcessId,
            maximumTraceBytes = MaximumTraceBytes,
        }, JsonOptions));
        return true;
    }

    private static void WriteLine(string line)
    {
        if (_writer == null)
        {
            return;
        }

        _writer.WriteLine(line);
        _bytesWritten += Encoding.UTF8.GetByteCount(line) + 1;
    }

    private static void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _writer = null;
        }
    }

    private sealed class CenturionTraceRecord
    {
        public DateTimeOffset Utc { get; init; }
        public double ElapsedMilliseconds { get; init; }
        public string Direction { get; init; } = string.Empty;
        public string ProductId { get; init; } = string.Empty;
        public string EndpointPathHash { get; init; } = string.Empty;
        public string Frame { get; init; } = string.Empty;
        public string? ReportId { get; init; }
        public string? DeviceAddress { get; init; }
        public string? Payload { get; init; }
    }
}
