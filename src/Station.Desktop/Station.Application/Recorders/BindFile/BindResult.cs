namespace Station.Application.Recorders;

/// <summary>绑定结果。</summary>
public sealed record BindResult(
    bool IsSuccess,
    BindStatus Status,
    string? Message)
{
    public static BindResult Ok() => new(true, BindStatus.Success, null);

    public static BindResult Fail(BindStatus status, string message)
        => new(false, status, message);
}

/// <summary>绑定失败原因。</summary>
public enum BindStatus
{
    /// <summary>成功。</summary>
    Success = 0,

    /// <summary>记录仪不在台账。</summary>
    RecorderNotFound = 1,

    /// <summary>记录仪已停用。</summary>
    RecorderInactive = 2,

    /// <summary>记录仪已软删除。</summary>
    RecorderDeleted = 3,

    /// <summary>用户不在册 / 已禁用 / 工号为空。</summary>
    UserInactive = 4,

    /// <summary>部门不在册 / 已禁用 / 编码为空。</summary>
    DeptInactive = 5,

    /// <summary>记录仪未接入（无法写绑定文件）。</summary>
    DeviceNotOnline = 6,

    /// <summary>写绑定文件失败。</summary>
    WriteFailed = 7
}
