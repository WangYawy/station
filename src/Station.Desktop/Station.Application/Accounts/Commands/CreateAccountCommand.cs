namespace Station.Application.Accounts.Commands;

/// <summary>创建账号命令。</summary>
public sealed class CreateAccountCommand
{
    public long UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
