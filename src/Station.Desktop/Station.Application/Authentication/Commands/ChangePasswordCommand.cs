namespace Station.Application.Authentication.Commands;

/// <summary>用户自助修改密码命令。</summary>
public sealed class ChangePasswordCommand
{
    public long AccountId { get; init; }
    public string OldPassword { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}
