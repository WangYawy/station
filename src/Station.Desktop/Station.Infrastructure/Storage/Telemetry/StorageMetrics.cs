using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Station.Infrastructure.Storage.Telemetry;

/// <summary>
/// 存储相关指标（.NET 内置 System.Diagnostics.Metrics）。
/// 命名遵循 OpenTelemetry 语义约定：{area}.{entity}.{metric}。
/// 采集端通过 MeterProvider 订阅 "Station.Storage" 即可。
/// </summary>
public sealed class StorageMetrics : IDisposable
{
    public const string MeterName = "Station.Storage";

    private readonly Meter _meter;
    private readonly Counter<long> _uploadTotal;
    private readonly Counter<long> _uploadErrors;
    private readonly Histogram<double> _uploadDurationMs;
    private readonly Counter<long> _uploadBytes;
    private readonly Counter<long> _circuitOpenTotal;
    private readonly Counter<long> _retryTotal;

    public StorageMetrics(IMeterFactory factory)
    {
        _meter = factory.Create(MeterName);

        _uploadTotal = _meter.CreateCounter<long>("storage.upload.total",
            description: "上传总次数");
        _uploadErrors = _meter.CreateCounter<long>("storage.upload.errors",
            description: "上传失败次数");
        _uploadDurationMs = _meter.CreateHistogram<double>("storage.upload.duration",
            unit: "ms", description: "上传耗时分布");
        _uploadBytes = _meter.CreateCounter<long>("storage.upload.bytes",
            unit: "By", description: "上传字节数");
        _circuitOpenTotal = _meter.CreateCounter<long>("storage.circuit.open",
            description: "熔断打开次数");
        _retryTotal = _meter.CreateCounter<long>("storage.retry.total",
            description: "重试总次数");
    }

    public void RecordUpload(string target, bool success, double elapsedMs, long bytes)
    {
        var tags = new TagList { { "target", target }, { "success", success } };
        _uploadTotal.Add(1, tags);
        if (!success) _uploadErrors.Add(1, tags);
        _uploadDurationMs.Record(elapsedMs, tags);
        if (success && bytes > 0) _uploadBytes.Add(bytes, tags);
    }

    public void RecordCircuitOpen(string target) =>
        _circuitOpenTotal.Add(1, new TagList { { "target", target } });

    public void RecordRetry(string target) =>
        _retryTotal.Add(1, new TagList { { "target", target } });

    public void Dispose() => _meter.Dispose();
}
