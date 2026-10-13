namespace Station.Application.Authentication;

/// <summary>登录结果。</summary>
public sealed record LoginResult(
    bool Success,
    LoginFailureReason? FailureReason,
    AuthSession? Session,
    DateTime? LockedUntil = null);

public enum LoginFailureReason
{
    InvalidCredentials,
    Disabled,
    LockedOut
}
