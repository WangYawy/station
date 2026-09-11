using Microsoft.Extensions.Options;
using Station.Domain.Authorization;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Domain.Repositories;
using Station.Domain.Security;
using Station.Infrastructure.Db;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;

namespace Station.Infrastructure.Persistence;

/// <summary>认证相关表结构与种子数据初始化。</summary>
public interface IAuthSeeder
{
    Task EnsureAsync();
}

public sealed class AuthSeeder : IAuthSeeder
{
    private readonly IDatabaseInitializer _initializer;
    private readonly IRepository<Role> _roles;
    private readonly IRepository<Permission> _permissions;
    private readonly IRepository<RolePermission> _rolePermissions;
    private readonly IRepository<Account> _accounts;
    private readonly IRepository<Dept> _depts;
    private readonly IRepository<User> _users;
    private readonly IRepository<UserRole> _userRoles;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AuthSeedOptions _options;

    public AuthSeeder(
        IDatabaseInitializer initializer,
        IRepository<Role> roles,
        IRepository<Permission> permissions,
        IRepository<RolePermission> rolePermissions,
        IRepository<Account> accounts,
        IRepository<Dept> depts,
        IRepository<User> users,
        IRepository<UserRole> userRoles,
        IPasswordHasher passwordHasher,
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
        _passwordHasher = passwordHasher;
        _options = options.Value;
    }

    public async Task EnsureAsync()
    {
        _initializer.EnsureCreated(AuthSeedData.EntityTypes);

        // 权限目录（幂等升级：目录为空时全量播种，否则仅补新增权限码）
        if (!await _permissions.IsAnyAsync(_ => true))
        {
            var seed = PermissionCodes.Catalog.Select((p, i) => new Permission
            {
                Id = i + 1,
                Code = p.Code,
                Name = p.Name,
                Module = p.Module
            }).ToList();
            await _permissions.InsertRangeAsync(seed);
        }
        else
        {
            var existing = await _permissions.GetListAsync();
            var existingCodes = existing.Select(p => p.Code).ToHashSet();
            var missing = PermissionCodes.Catalog.Where(p => !existingCodes.Contains(p.Code)).ToList();
            if (missing.Count > 0)
            {
                var maxId = existing.Max(p => p.Id);
                await _permissions.InsertRangeAsync(missing.Select((p, i) => new Permission
                {
                    Id = maxId + i + 1,
                    Code = p.Code,
                    Name = p.Name,
                    Module = p.Module
                }).ToList());
            }
        }

        // 预置角色 + 角色权限（幂等：缺失角色补建，系统角色按目录补齐缺失权限）
        var roles = await _roles.GetListAsync();
        var nextRoleId = roles.Count > 0 ? roles.Max(r => r.Id) + 1 : 1;
        var allLinks = await _rolePermissions.GetListAsync();
        var nextLinkId = allLinks.Count > 0 ? allLinks.Max(x => x.Id) + 1 : 1;
        foreach (var preset in PresetRoles.List)
        {
            var role = await _roles.FirstAsync(r => r.Code == preset.Code);
            if (role is null)
            {
                role = new Role
                {
                    Id = nextRoleId++,
                    Code = preset.Code,
                    Name = preset.Name,
                    DataScope = preset.DataScope,
                    IsSystem = true
                };
                await _roles.InsertAsync(role);
            }

            var presetPerms = await _permissions.GetListAsync(p => preset.Permissions.Contains(p.Code));
            var granted = await _rolePermissions.GetListAsync(rp => rp.RoleId == role.Id);
            var grantedPermissionIds = granted.Select(x => x.PermissionId).ToHashSet();
            var missingLinks = presetPerms
                .Where(p => !grantedPermissionIds.Contains(p.Id))
                .Select(p => new RolePermission { Id = nextLinkId++, RoleId = role.Id, PermissionId = p.Id })
                .ToList();
            if (missingLinks.Count > 0)
            {
                await _rolePermissions.InsertRangeAsync(missingLinks);
            }
        }

        // 根部门
        long rootDeptId;
        if (!await _depts.IsAnyAsync(d => d.Code == RootDept.Code))
        {
            var root = new Dept
            {
                Id = 1,
                Code = RootDept.Code,
                Name = RootDept.Name,
                ParentId = null,
                SortOrder = 0
            };
            await _depts.InsertAsync(root);
            rootDeptId = root.Id;
        }
        else
        {
            rootDeptId = (await _depts.FirstAsync(d => d.Code == RootDept.Code))!.Id;
        }

        // 系统管理员用户（挂 admin 角色，承载"管理员"数据范围）
        long adminUserId;
        if (!await _users.IsAnyAsync(u => u.UserNo == _options.DefaultAdminUserName))
        {
            var adminUser = new User
            {
                Id = 1,
                UserNo = _options.DefaultAdminUserName,
                Name = "系统管理员",
                DeptId = rootDeptId,
                IsActive = true
            };
            await _users.InsertAsync(adminUser);
            adminUserId = adminUser.Id;

            var adminRole = await _roles.FirstAsync(r => r.Code == PresetRoles.Admin);
            if (adminRole is not null)
            {
                await _userRoles.InsertAsync(new UserRole { Id = 1, UserId = adminUserId, RoleId = adminRole.Id });
            }
        }
        else
        {
            adminUserId = (await _users.FirstAsync(u => u.UserNo == _options.DefaultAdminUserName))!.Id;
        }

        // 默认管理员账号
        if (!await _accounts.IsAnyAsync(a => a.UserName == _options.DefaultAdminUserName))
        {
            await _accounts.InsertAsync(new Account
            {
                Id = 1,
                UserName = _options.DefaultAdminUserName,
                PasswordHash = _passwordHasher.Hash(_options.DefaultAdminPassword),
                UserId = adminUserId,
                IsEnabled = true
            });
        }
    }
}

/// <summary>种子账号配置，对应配置节 <c>Station:Auth</c>。</summary>
public sealed class AuthSeedOptions
{
    public string DefaultAdminUserName { get; set; } = "admin";

    public string DefaultAdminPassword { get; set; } = "Admin@123";
}
