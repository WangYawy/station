using Station.Contracts;
using Station.Device.FileSystem;

namespace Station.Device.Runtime.FileSystem;

/// <summary>
/// 默认协议路由：构造时按 <see cref="ProtocolType"/> 建索引，O(1) 查找。
/// 未注册协议时抛 <see cref="NotSupportedException"/>。
/// </summary>
public sealed class DefaultFileSystemProvider : IDeviceFileSystemProvider
{
    private readonly IReadOnlyDictionary<ProtocolType, IDeviceFileSystem> _map;

    public DefaultFileSystemProvider(IEnumerable<IDeviceFileSystem> fileSystems)
    {
        var dict = new Dictionary<ProtocolType, IDeviceFileSystem>();
        foreach (var fs in fileSystems)
        {
            // 同协议多实现时保留先注册的
            if (!dict.ContainsKey(fs.Protocol))
                dict[fs.Protocol] = fs;
        }
        _map = dict;
    }

    public IDeviceFileSystem GetFor(ProtocolType protocol)
    {
        if (_map.TryGetValue(protocol, out var fs)) return fs;
        throw new NotSupportedException($"未注册协议 {protocol} 的设备文件系统实现");
    }
}
