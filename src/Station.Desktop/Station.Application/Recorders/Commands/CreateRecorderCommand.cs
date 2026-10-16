using Station.Contracts;

namespace Station.Application.Recorders.Commands;

/// <summary>新增记录仪台账。</summary>
public sealed class CreateRecorderCommand
{
    /// <summary>序列号（跨端自然键，创建后不可变）。</summary>
    public string SerialNumber { get; init; } = string.Empty;

    /// <summary>设备编码（可选，用于展示）。</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>记录仪名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>型号。</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>协议（UMS / MTP / PrivateSdk）。</summary>
    public ProtocolType Protocol { get; init; } = ProtocolType.Ums;

    /// <summary>是否授权（默认未授权，绑定后由管理员开启）。</summary>
    public bool IsAuthorized { get; init; }

    /// <summary>操作人。</summary>
    public long? CreatedBy { get; init; }
}
