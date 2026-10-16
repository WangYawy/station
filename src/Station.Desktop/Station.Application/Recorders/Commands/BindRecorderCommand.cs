namespace Station.Application.Recorders.Commands;

/// <summary>绑定记录仪到用户 / 部门。</summary>
public sealed class BindRecorderCommand
{
    /// <summary>台账记录仪 ID。</summary>
    public long RecorderId { get; init; }

    /// <summary>绑定用户 ID。</summary>
    public long UserId { get; init; }

    /// <summary>绑定部门 ID。</summary>
    public long DeptId { get; init; }

    /// <summary>操作人。</summary>
    public long? UpdatedBy { get; init; }
}
