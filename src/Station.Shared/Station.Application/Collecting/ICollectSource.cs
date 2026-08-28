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

    /// <summary>记录仪根目录（UMS=盘符根；模拟源=模拟目录），用于绑定 ini 读写。</summary>
    string GetRecorderRoot(CollectDeviceInfo device);

    /// <summary>
    /// 扫描记录仪文件
    /// </summary>
    Task<IReadOnlyList<SourceFileInfo>> ScanAsync(CollectDeviceInfo device, CancellationToken cancellationToken);

    /// <summary>复制文件到目标路径，onProgress 回调 0~1；device 携带该设备根路径（UMS 多盘按设备路由）。</summary>
    Task CopyAsync(
        CollectDeviceInfo device,
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken);

    /// <summary>
    /// 擦除记录仪文件
    /// </summary>
    Task EraseAsync(CollectDeviceInfo device, CancellationToken cancellationToken);
}
