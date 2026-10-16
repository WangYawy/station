using Station.Contracts;
using Station.Device.Detection;

namespace Station.Device.FileSystem;

/// <summary>
/// 设备文件系统访问抽象。
/// 一个协议一份实现（UMS / MTP / PrivateSdk / Simulated）。
/// 所有方法只接收 <see cref="DetectedDevice"/>，不感知业务归属。
/// </summary>
public interface IDeviceFileSystem
{
    /// <summary>该文件系统对应的协议。</summary>
    ProtocolType Protocol { get; }

    // ========== 采集语义 ==========

    /// <summary>设备根路径（用于显示 / 绑定文件读写）。</summary>
    string GetRecorderRoot(DetectedDevice device);

    /// <summary>扫描设备上所有可采集文件。</summary>
    Task<IReadOnlyList<SourceFileInfo>> ScanAsync(DetectedDevice device, CancellationToken ct);

    /// <summary>从设备复制文件到本地，onProgress 回调 0~1。</summary>
    Task CopyAsync(
        DetectedDevice device,
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken ct);

    /// <summary>擦除设备上的文件；返回成功擦除数量。</summary>
    Task<int> EraseAsync(
        DetectedDevice device,
        IEnumerable<SourceFileInfo> files,
        CancellationToken ct);

    // ========== 文件 IO（供绑定文件等使用）==========

    /// <summary>判断相对路径下的文件是否存在。</summary>
    bool FileExists(DetectedDevice device, string relativePath);

    /// <summary>读取文本内容；不存在或读取失败返回 null。</summary>
    string? ReadAllText(DetectedDevice device, string relativePath);

    /// <summary>写入文本内容；父目录不存在时自动创建。</summary>
    void WriteAllText(DetectedDevice device, string relativePath, string content);

    /// <summary>删除文件；不存在时静默忽略。</summary>
    void DeleteFile(DetectedDevice device, string relativePath);
}
