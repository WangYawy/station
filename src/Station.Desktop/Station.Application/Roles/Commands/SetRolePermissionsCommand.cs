namespace Station.Application.Roles.Commands;

/// <summary>设置角色权限（全量替换）。</summary>
public sealed record SetRolePermissionsCommand(long RoleId, IReadOnlyList<long> PermissionIds);
