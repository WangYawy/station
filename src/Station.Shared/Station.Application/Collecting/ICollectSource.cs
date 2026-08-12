namespace Station.Application.Collecting;

/// <summary>采集源文件信息。</summary>
public sealed record SourceFileInfo(string RelativePath, string FileName, long Size, DateTime ModifiedAt)
{
    public string Fingerprint => $"{FileName}|{Size}|{ModifiedAt.Ticks}";
}

/// <summary>
/// 记录仪采集源抽象：UMS / MTP / 私有加密（SDK 转 U 盘）统一接口。
/// M7 提供 <see cref="SimulatedCollectSource"/> 用于无硬件开发验证。
/// </summary>
public interface ICollectSource
{
    string SourceKey { get; }

    Task<IReadOnlyList<SourceFileInfo>> ScanAsync(CollectDeviceInfo device, CancellationToken cancellationToken);

    /// <summary>复制文件到目标路径，onProgress 回调 0~1。</summary>
    Task CopyAsync(SourceFileInfo file, string destinationPath, Func<double, Task>? onProgress, CancellationToken cancellationToken);

    Task EraseAsync(CollectDeviceInfo device, CancellationToken cancellationToken);
}
