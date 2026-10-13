using Microsoft.Extensions.Options;
using Station.Application.Security;
using Station.Data;
using Station.Data.IdGeneration;
using Station.Domain.Accounts;
using Station.Domain.Authorization;
using Station.Domain.Departments;
using Station.Domain.Permissions;
using Station.Domain.Roles;
using Station.Domain.Users;

namespace Station.Infrastructure.Persistence;

public interface IAuthSeeder
{
    Task EnsureAsync(CancellationToken ct = default);
}

public sealed class AuthSeeder : IAuthSeeder
{
    private readonly IDatabaseInitializer _initializer;
    private readonly IRoleRepository _roles;
    private readonly IPermissionRepository _permissions;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IAccountRepository _accounts;
    private readonly IDeptRepository _depts;
    private readonly IUserRepository _users;
    private readonly IUserRoleRepository _userRoles;
    private readonly IPasswordService _passwordService;
    private readonly IIdGenerator _idGenerator;
    private readonly AuthSeedOptions _options;

    public AuthSeeder(
        IDatabaseInitializer initializer,
        IRoleRepository roles,
        IPermissionRepository permissions,
        IRolePermissionRepository rolePermissions,
        IAccountRepository accounts,
        IDeptRepository depts,
        IUserRepository users,
        IUserRoleRepository userRoles,
        IPasswordService passwordService,
        IIdGenerator idGenerator,
        IOptions<AuthSeedOptions> options)
    {
        _initializer = initializer;
        _roles = roles;
        _permissions = permissions;
        _rolePermissions = rolePermissions;
        _accounts = accounts;
        _depts = depts;
        _users = users;
        _userRoles = userRoles;
        _passwordService = passwordService;
        _idGenerator = idGenerator;
        _options = options.Value;
    }

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        _initializer.EnsureCreated(AuthSeedData.EntityTypes);

        var now = DateTime.Now;

        // ---- 权限目录 ----
        if (!await _permissions.AnyAsync(ct))
        {
            var seed = PermissionCodes.Catalog
                .Select(p => Permission.Create(
                    _idGenerator.NewId(), p.Code, p.Name, p.Module, now))
                .ToList();
            await _permissions.AddRangeAsync(seed, ct);
        }
        else
        {
            var existing = await _permissions.GetListAsync(ct);
            var existingCodes = existing.Select(p => p.Code).ToHashSet();
            var missing = PermissionCodes.Catalog
                .Where(p => !existingCodes.Contains(p.Code))
                .Select(p => Permission.Create(
                    _idGenerator.NewId(), p.Code, p.Name, p.Module, now))
                .ToList();
            if (missing.Count > 0)
            {
                await _permissions.AddRangeAsync(missing, ct);
            }
        }

        // ---- 预置角色 + 角色权限 ----
        foreach (var preset in PresetRoles.List)
        {
            var role = await _roles.GetByCodeAsync(preset.Code, ct);
            if (role is null)
            {
                role = Role.Create(
                    _idGenerator.NewId(), preset.Code, preset.Name,
                    preset.DataScope, isSystem: true, now);
                await _roles.AddAsync(role, ct);
            }

            var presetPerms = await _permissions.GetByCodesAsync(preset.Permissions, ct);
            var granted = await _rolePermissions.GetPermissionIdsByRoleIdAsync(role.Id, ct);
            var grantedSet = granted.ToHashSet();

            var missingPermissionIds = presetPerms
                .Where(p => !grantedSet.Contains(p.Id))
                .Select(p => p.Id)
                .ToList();

            if (missingPermissionIds.Count > 0)
            {
                await _rolePermissions.AddRangeAsync(role.Id, missingPermissionIds, ct);
            }
        }

        // ---- 根部门 ----
        long rootDeptId;
        var rootDept = await _depts.GetByCodeAsync(RootDept.Code, ct);
        if (rootDept is null)
        {
            rootDept = Dept.Create(
                _idGenerator.NewId(), RootDept.Code, RootDept.Name,
                parentId: null, sortOrder: 0, now);
            await _depts.AddAsync(rootDept, ct);
            rootDeptId = rootDept.Id;
        }
        else
        {
            rootDeptId = rootDept.Id;
        }

        // ---- 系统管理员用户 ----
        long adminUserId;
        var adminUser = await _users.GetByUserNoAsync(_options.DefaultAdminUserName, ct);
        if (adminUser is null)
        {
            adminUser = User.Create(
                _idGenerator.NewId(), _options.DefaultAdminUserName,
                "系统管理员", rootDeptId, now);
            await _users.AddAsync(adminUser, ct);
            adminUserId = adminUser.Id;

            var adminRole = await _roles.GetByCodeAsync(PresetRoles.Admin, ct);
            if (adminRole is not null)
            {
                await _userRoles.AddAsync(adminUserId, adminRole.Id, ct);
            }
        }
        else
        {
            adminUserId = adminUser.Id;
        }

        // ---- 默认管理员账号 ----
        if (!await _accounts.ExistsUserNameAsync(_options.DefaultAdminUserName, ct))
        {
            var account = Account.Create(
                _idGenerator.NewId(),
                _options.DefaultAdminUserName,
                await _passwordService.HashAsync(_options.DefaultAdminPassword),
                adminUserId,
                now);

            await _accounts.AddAsync(account, ct);
        }
    }
}

public sealed class AuthSeedOptions
{
    public string DefaultAdminUserName { get; set; } = "admin";
    public string DefaultAdminPassword { get; set; } = "Admin@123";
}
