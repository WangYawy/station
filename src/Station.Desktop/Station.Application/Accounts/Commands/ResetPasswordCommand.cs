namespace Station.Application.Accounts.Commands;

/// <summary>管理员重置账号密码命令。</summary>
public sealed record ResetPasswordCommand(long AccountId, string NewPassword);
