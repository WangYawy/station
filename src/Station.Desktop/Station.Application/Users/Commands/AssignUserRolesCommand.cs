namespace Station.Application.Users.Commands;

/// <summary>为用户分配角色（全量替换）。</summary>
public sealed record AssignUserRolesCommand(long UserId, IReadOnlyList<long> RoleIds);
