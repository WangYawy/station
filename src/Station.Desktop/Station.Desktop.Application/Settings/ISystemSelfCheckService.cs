using Station.Application.Settings;

namespace Station.Desktop.Application.Settings;

/// <summary>设备自检：USB/MTP 设备、磁盘读写、网络、存储目标。</summary>
public interface ISystemSelfCheckService
{
    Task<IReadOnlyList<SelfCheckItemDto>> RunAsync();
}
