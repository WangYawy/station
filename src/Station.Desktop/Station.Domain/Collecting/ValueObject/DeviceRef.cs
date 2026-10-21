using Station.Contracts;

namespace Station.Domain.Collecting;

/// <summary>
/// 设备引用值对象。
///
/// 【作用】
///   - 把 DetectedDevice 的物理信息"快照"成 Domain 原生值对象
///   - 跨进程通过拆列持久化恢复，不依赖 Station.Device
///   - 由 Application 层工厂转换为 DetectedDevice 供执行时使用
///
/// 【字段】
///   - Key：路由标识（PORT:xxx / SN:xxx / ROOT:xxx）
///   - PhysicalPortId：物理端口原始标识（可空，降级时为 null）
///   - Serial：序列号（可空）
///   - Root：挂载根路径
///   - Name：设备显示名
///   - Protocol：协议类型
///
/// 【不变式】
///   - Key 非空
///   - Root 非空
///   - Name 非空
/// </summary>
public sealed record DeviceRef
{
    /// <summary>路由标识，如 PORT:WIN:HUB0001:PORT0003。</summary>
    public string Key { get; }

    /// <summary>物理端口原始标识，如 WIN:HUB0001:PORT0003；降级时为 null。</summary>
    public string? PhysicalPortId { get; }

    /// <summary>序列号；部分设备可能为 null。</summary>
    public string? Serial { get; }

    /// <summary>挂载根路径（UMS=盘符；MTP=虚拟根）。</summary>
    public string Root { get; }

    /// <summary>设备显示名。</summary>
    public string Name { get; }

    /// <summary>协议类型。</summary>
    public ProtocolType Protocol { get; }

    private DeviceRef(
        string key,
        string? physicalPortId,
        string? serial,
        string root,
        string name,
        ProtocolType protocol)
    {
        Key = key;
        PhysicalPortId = physicalPortId;
        Serial = serial;
        Root = root;
        Name = name;
        Protocol = protocol;
    }

    /// <summary>
    /// 创建设备引用。校验关键字段非空；写库前的一致性由 Application 层保证。
    /// </summary>
    public static DeviceRef Create(
        string key,
        string? physicalPortId,
        string? serial,
        string root,
        string name,
        ProtocolType protocol)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("DeviceRef.Key 不能为空", nameof(key));
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DeviceRef.Root 不能为空", nameof(root));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("DeviceRef.Name 不能为空", nameof(name));

        return new DeviceRef(
            key.Trim(),
            string.IsNullOrWhiteSpace(physicalPortId) ? null : physicalPortId.Trim(),
            string.IsNullOrWhiteSpace(serial) ? null : serial.Trim(),
            root.Trim(),
            name.Trim(),
            protocol);
    }

    /// <summary>判断是否存在物理端口标识。</summary>
    public bool HasPhysicalPort => !string.IsNullOrEmpty(PhysicalPortId);
}
