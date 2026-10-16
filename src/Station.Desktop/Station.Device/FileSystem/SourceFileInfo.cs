namespace Station.Device.FileSystem;

/// <summary>
/// 设备侧文件元信息。
/// </summary>
/// <param name="RelativePath">相对于设备根的路径。</param>
/// <param name="FileName">文件名。</param>
/// <param name="Size">字节大小。</param>
/// <param name="ModifiedAt">最后修改时间。</param>
/// <param name="ObjectPath">MTP 专用：便携设备对象 ID；UMS 为 null。</param>
public sealed record SourceFileInfo(
    string RelativePath,
    string FileName,
    long Size,
    DateTime ModifiedAt,
    string? ObjectPath = null)
{
    /// <summary>文件指纹：文件名 + 大小 + 修改时间，用于跳过已采集。</summary>
    public string Fingerprint => $"{FileName}|{Size}|{ModifiedAt.Ticks}";
}
