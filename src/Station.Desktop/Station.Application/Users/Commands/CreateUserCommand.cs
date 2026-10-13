namespace Station.Application.Users.Commands;

/// <summary>
/// 创建用户命令。
/// 可选同时创建登录账号：UserName + Password 同时非空时创建。
/// </summary>
public sealed class CreateUserCommand
{
    public string UserNo { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long DeptId { get; init; }
    public bool IsActive { get; init; } = true;

    // ---- 可选：创建登录账号 ----
    public string? UserName { get; init; }
    public string? Password { get; init; }
}
