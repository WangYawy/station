namespace Station.Application.Users.Commands;

/// <summary>更新用户命令。</summary>
public sealed class UpdateUserCommand
{
    public long Id { get; init; }
    public string UserNo { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long DeptId { get; init; }
    public bool IsActive { get; init; }
}
