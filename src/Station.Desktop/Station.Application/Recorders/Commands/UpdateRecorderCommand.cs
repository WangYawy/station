using Station.Contracts;

namespace Station.Application.Recorders.Commands;

/// <summary>修改记录仪台账基础信息（不含序列号）。</summary>
public sealed class UpdateRecorderCommand
{
    public long Id { get; init; }

    /// <summary>名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>编码（可选）。</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>型号。</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>协议。</summary>
    public ProtocolType Protocol { get; init; }

    /// <summary>绑定用户（null 表示解绑）。</summary>
    public long? BoundUserId { get; init; }

    /// <summary>绑定部门（null 表示解绑）。</summary>
    public long? DeptId { get; init; }

    /// <summary>是否授权。</summary>
    public bool IsAuthorized { get; init; }

    /// <summary>是否启用。</summary>
    public bool IsActive { get; init; }

    /// <summary>操作人。</summary>
    public long? UpdatedBy { get; init; }
}
