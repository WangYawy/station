using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Station.Application.Storage;

namespace Station.Infrastructure.Storage.Telemetry;

/// <summary>
/// 遥测装饰器（最内层，紧贴具体实现）：
///   - 记录每次操作耗时与结果到 Metrics；
///   - 输出结构化日志（含 TraceId/TargetName/RemotePath）。
/// </summary>
public sealed class TelemetryStorageTarget : IStorageTarget
{
    private readonly IStorageTarget _inner;
    private readonly StorageMetrics _metrics;
    private readonly ILogger _logger;

    public TelemetryStorageTarget(IStorageTarget inner, StorageMetrics metrics, ILogger logger)
    {
        _inner = inner;
        _metrics = metrics;
        _logger = logger;
    }

    public string Name => _inner.Name;
    public StorageTargetKind Kind => _inner.Kind;

    public async Task UploadAsync(UploadTargetFile file, Func<double, Task>? onProgress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.UploadAsync(file, onProgress, ct).ConfigureAwait(false);
            sw.Stop();
            _metrics.RecordUpload(_inner.Name, true, sw.Elapsed.TotalMilliseconds, file.Size);
            _logger.LogInformation(
                "[{Target}] 上传成功：{RemotePath}（{Size} 字节，{Elapsed} ms）",
                _inner.Name, file.RemotePath, file.Size, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _metrics.RecordUpload(_inner.Name, false, sw.Elapsed.TotalMilliseconds, 0);
            _logger.LogWarning(ex,
                "[{Target}] 上传失败：{RemotePath}（{Elapsed} ms）",
                _inner.Name, file.RemotePath, sw.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var size = await _inner.GetRemoteSizeAsync(remotePath, ct).ConfigureAwait(false);
            _logger.LogDebug("[{Target}] 远端大小 {RemotePath} = {Size}（{Elapsed} ms）",
                _inner.Name, remotePath, size, sw.ElapsedMilliseconds);
            return size;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[{Target}] 获取远端大小失败：{RemotePath}", _inner.Name, remotePath);
            throw;
        }
    }
    public Task<RemoteDigestResult> ComputeRemoteDigestAsync(string remotePath, CancellationToken ct) =>
       _inner.ComputeRemoteDigestAsync(remotePath, ct);

    public Task<RemoteSignatureResult> SignRemoteAsync(string remotePath, string privateKeyPem, CancellationToken ct) =>
         _inner.SignRemoteAsync(remotePath, privateKeyPem, ct);

    public Task DeleteAsync(string remotePath, CancellationToken ct) =>
        _inner.DeleteAsync(remotePath, ct);
}
