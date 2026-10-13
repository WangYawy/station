namespace Station.Application.Authentication;

/// <summary>修改密码结果。</summary>
public sealed record ChangePasswordResult(bool Success, string? Message = null);
