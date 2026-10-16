using Station.Contracts;

namespace Station.Device.FileSystem;

/// <summary>
/// 按协议路由到具体 <see cref="IDeviceFileSystem"/>。
/// 单例，无状态，O(1) 查找。
/// </summary>
public interface IDeviceFileSystemProvider
{
    /// <summary>获取指定协议的设备文件系统实现。</summary>
    IDeviceFileSystem GetFor(ProtocolType protocol);
}
