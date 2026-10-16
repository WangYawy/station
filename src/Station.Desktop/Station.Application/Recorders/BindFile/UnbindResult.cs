namespace Station.Application.Recorders;

/// <summary>解绑结果。</summary>
public sealed record UnbindResult(
    bool IsSuccess,
    UnbindStatus Status,
    string? Message)
{
    public static UnbindResult Ok() => new(true, UnbindStatus.Success, null);

    public static UnbindResult Fail(UnbindStatus status, string message)
        => new(false, status, message);
}

/// <summary>解绑失败原因。</summary>
public enum UnbindStatus
{
    /// <summary>成功。</summary>
    Success = 0,

    /// <summary>记录仪不在台账。</summary>
    RecorderNotFound = 1,

    /// <summary>记录仪未绑定。</summary>
    NotBound = 2
}
