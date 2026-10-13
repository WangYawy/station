using Station.Application.Roles.Commands;
using Station.Application.Users;
using Station.Data.IdGeneration;
using Station.Data.Repositories;
using Station.Domain.Roles;
using Station.Domain.Users;

namespace Station.Application.Roles;

public sealed class RoleService : IRoleService
{
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IUserRoleRepository _userRoles;    // 删角色时清理关联
    private readonly IRoleQueryService _roleQuery;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIdGenerator _idGenerator;

    public RoleService(
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        IUserRoleRepository userRoles,
        IRoleQueryService roleQuery,
        IUnitOfWork unitOfWork,
        IIdGenerator idGenerator)
    {
        _roles = roles;
        _rolePermissions = rolePermissions;
        _userRoles = userRoles;
        _roleQuery = roleQuery;
        _unitOfWork = unitOfWork;
        _idGenerator = idGenerator;
    }

    public async Task<long> CreateAsync(
        CreateRoleCommand command, CancellationToken ct = default)
    {
        if (await _roleQuery.ExistsCodeAsync(command.Code, ct))
            throw new InvalidOperationException($"角色编码 {command.Code} 已存在");

        var now = DateTime.Now;
        var role = Role.Create(
            _idGenerator.NewId(), command.Code, command.Name,
            command.DataScope, isSystem: false, now);

        if (!command.IsActive) role.Deactivate(now);

        await _roles.AddAsync(role, ct);
        return role.Id;
    }

    public async Task<OpResult> UpdateAsync(
        UpdateRoleCommand command, CancellationToken ct = default)
    {
        var role = await _roles.GetByIdAsync(command.Id, ct);
        if (role is null) return new OpResult(false, "角色不存在");

        var now = DateTime.Now;
        if (role.Code != command.Code)
        {
            if (role.IsSystem) return new OpResult(false, "预置角色编码不可修改");
            role.ChangeCode(command.Code, now);
        }
        role.Rename(command.Name, now);
        role.ChangeDataScope(command.DataScope, now);
        if (command.IsActive) role.Activate(now); else role.Deactivate(now);

        await _roles.UpdateAsync(role, ct);
        return new OpResult(true);
    }

    public async Task<OpResult> DeleteAsync(long id, CancellationToken ct = default)
    {
        var role = await _roles.GetByIdAsync(id, ct);
        if (role is null) return new OpResult(false, "角色不存在");
        if (role.IsSystem) return new OpResult(false, "预置角色不可删除");

        await _unitOfWork.UseTranAsync(async () =>
        {
            await _rolePermissions.DeleteByRoleIdAsync(id, ct);
            await _userRoles.DeleteByRoleIdAsync(id, ct);
            await _roles.DeleteAsync(id, ct);
            return true;
        });

        return new OpResult(true);
    }

    public async Task<OpResult> SetPermissionsAsync(
        SetRolePermissionsCommand command, CancellationToken ct = default)
    {
        if (!await _roleQuery.ExistsAsync(command.RoleId, ct))
            return new OpResult(false, "角色不存在");

        await _unitOfWork.UseTranAsync(async () =>
        {
            await _rolePermissions.DeleteByRoleIdAsync(command.RoleId, ct);
            await _rolePermissions.AddRangeAsync(command.RoleId, command.PermissionIds, ct);
            return true;
        });

        return new OpResult(true);
    }
}
