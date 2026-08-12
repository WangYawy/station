using Microsoft.Extensions.Options;
using Station.Domain.Entities;
using Station.Domain.Enums;
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

        // 权限目录
        if (!await _permissions.IsAnyAsync(_ => true))
        {
            var catalog = PermissionCodes.Catalog.Select((p, i) => new Permission
            {
                Id = i + 1,
                Code = p.Code,
                Name = p.Name,
                Module = p.Module
            }).ToList();
            await _permissions.InsertRangeAsync(catalog);
        }

        // 预置角色 + 角色权限
        if (!await _roles.IsAnyAsync(_ => true))
        {
            var roleId = 0L;
            var links = new List<RolePermission>();
            var linkId = 0L;
            foreach (var preset in AuthSeedData.PresetRoles)
            {
                roleId++;
                await _roles.InsertAsync(new Role
                {
                    Id = roleId,
                    Code = preset.Code,
                    Name = preset.Name,
                    DataScope = preset.DataScope,
                    IsSystem = true
                });

                var perms = await _permissions.GetListAsync(p => preset.Permissions.Contains(p.Code));
                foreach (var perm in perms)
                {
                    links.Add(new RolePermission { Id = ++linkId, RoleId = roleId, PermissionId = perm.Id });
                }
            }

            await _rolePermissions.InsertRangeAsync(links);
        }

        // 根部门
        long rootDeptId;
        if (!await _depts.IsAnyAsync(d => d.Code == AuthSeedData.RootDeptCode))
        {
            var root = new Dept
            {
                Id = 1,
                Code = AuthSeedData.RootDeptCode,
                Name = AuthSeedData.RootDeptName,
                ParentId = null,
                SortOrder = 0
            };
            await _depts.InsertAsync(root);
            rootDeptId = root.Id;
        }
        else
        {
            rootDeptId = (await _depts.FirstAsync(d => d.Code == AuthSeedData.RootDeptCode))!.Id;
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

            var adminRole = await _roles.FirstAsync(r => r.Code == AuthSeedData.AdminRoleCode);
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
