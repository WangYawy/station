using Station.Domain.Enums;

namespace Station.Application.Authentication;

public sealed record LoginRequest(string UserName, string Password, string? SourceIp = null);

public enum LoginFailureReason
{
    InvalidCredentials,
    Disabled,
    LockedOut
}

public sealed record LoginResult(
    bool Success,
    LoginFailureReason? FailureReason,
    AuthSession? Session,
    DateTime? LockedUntil = null);

public sealed record ChangePasswordResult(bool Success, string? Message = null);

/// <summary>当前登录会话：账号 + 人员 + 角色 + 权限 + 数据范围。</summary>
public sealed record AuthSession(
    long AccountId,
    long? UserId,
    string UserName,
    string? UserNo,
    string? Name,
    long? DeptId,
    DataScope DataScope,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
