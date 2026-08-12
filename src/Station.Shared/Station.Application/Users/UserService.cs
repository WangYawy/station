using Station.Application.Authorization;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;

namespace Station.Application.Users;

public sealed class UserService : IUserService
{
    private readonly IRepository<Dept> _depts;
    private readonly IRepository<User> _users;
    private readonly IRepository<Account> _accounts;
    private readonly IRepository<Role> _roles;
    private readonly IRepository<Permission> _permissions;
    private readonly IRepository<RolePermission> _rolePermissions;
    private readonly IRepository<UserRole> _userRoles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IIdGenerator _idGenerator;

    public UserService(
        IRepository<Dept> depts,
        IRepository<User> users,
        IRepository<Account> accounts,
        IRepository<Role> roles,
        IRepository<Permission> permissions,
        IRepository<RolePermission> rolePermissions,
        IRepository<UserRole> userRoles,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IIdGenerator idGenerator)
    {
        _depts = depts;
        _users = users;
        _accounts = accounts;
        _roles = roles;
        _permissions = permissions;
        _rolePermissions = rolePermissions;
        _userRoles = userRoles;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _idGenerator = idGenerator;
    }

    // ---------- 部门 ----------

    public async Task<IReadOnlyList<DeptDto>> GetDeptTreeAsync()
    {
        var list = await _depts.GetListAsync(d => d.IsActive);
        return list.OrderBy(d => d.SortOrder).Select(ToDto).ToList();
    }

    public async Task<long> CreateDeptAsync(DeptDto dto)
    {
        if (await _depts.IsAnyAsync(d => d.Code == dto.Code))
        {
            throw new InvalidOperationException($"部门编码 {dto.Code} 已存在");
        }

        var entity = new Dept
        {
            Id = _idGenerator.NextId(),
            Code = dto.Code,
            Name = dto.Name,
            ParentId = dto.ParentId,
            SortOrder = dto.SortOrder,
            IsActive = dto.IsActive
        };
        await _depts.InsertAsync(entity);
        return entity.Id;
    }

    public async Task<OpResult> UpdateDeptAsync(DeptDto dto)
    {
        if (dto.Id is not { } id)
        {
            return new OpResult(false, "缺少部门 ID");
        }

        var entity = await _depts.GetByIdAsync(id);
        if (entity is null)
        {
            return new OpResult(false, "部门不存在");
        }

        entity.Code = dto.Code;
        entity.Name = dto.Name;
        entity.ParentId = dto.ParentId;
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;
        await _depts.UpdateAsync(entity);
        return new OpResult(true);
    }

    public async Task<OpResult> DeleteDeptAsync(long id)
    {
        if (await _depts.IsAnyAsync(d => d.ParentId == id))
        {
            return new OpResult(false, "存在下级部门，无法删除");
        }

        if (await _users.IsAnyAsync(u => u.DeptId == id && u.IsActive))
        {
            return new OpResult(false, "部门下存在用户，无法删除");
        }

        await _depts.DeleteByIdAsync(id);
        return new OpResult(true);
    }

    // ---------- 用户 ----------

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync()
    {
        var list = await _users.GetListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<OpResult> CreateUserAsync(UserDto dto, string? userName = null, string? password = null)
    {
        if (await _users.IsAnyAsync(u => u.UserNo == dto.UserNo))
        {
            return new OpResult(false, $"工号 {dto.UserNo} 已存在");
        }

        if (userName is not null && await _accounts.IsAnyAsync(a => a.UserName == userName))
        {
            return new OpResult(false, $"登录名 {userName} 已存在");
        }

        var user = new User
        {
            Id = _idGenerator.NextId(),
            UserNo = dto.UserNo,
            Name = dto.Name,
            DeptId = dto.DeptId,
            IsActive = dto.IsActive
        };

        await _unitOfWork.UseTranAsync(async () =>
        {
            var users = _unitOfWork.GetRepository<User>();
            var accounts = _unitOfWork.GetRepository<Account>();
            await users.InsertAsync(user);
            if (userName is not null && password is not null)
            {
                await accounts.InsertAsync(new Account
                {
                    Id = _idGenerator.NextId(),
                    UserName = userName,
                    PasswordHash = _passwordHasher.Hash(password),
                    UserId = user.Id,
                    IsEnabled = true
                });
            }

            return true;
        });

        return new OpResult(true);
    }

    public async Task<OpResult> UpdateUserAsync(UserDto dto)
    {
        if (dto.Id is not { } id)
        {
            return new OpResult(false, "缺少用户 ID");
        }

        var entity = await _users.GetByIdAsync(id);
        if (entity is null)
        {
            return new OpResult(false, "用户不存在");
        }

        entity.UserNo = dto.UserNo;
        entity.Name = dto.Name;
        entity.DeptId = dto.DeptId;
        entity.IsActive = dto.IsActive;
        await _users.UpdateAsync(entity);
        return new OpResult(true);
    }

    public async Task<OpResult> DeleteUserAsync(long id)
    {
        var user = await _users.GetByIdAsync(id);
        if (user is null)
        {
            return new OpResult(false, "用户不存在");
        }

        await _unitOfWork.UseTranAsync(async () =>
        {
            var userRoles = _unitOfWork.GetRepository<UserRole>();
            var accounts = _unitOfWork.GetRepository<Account>();
            var users = _unitOfWork.GetRepository<User>();
            await userRoles.DeleteAsync(ur => ur.UserId == id);
            var accountList = await accounts.GetListAsync(a => a.UserId == id);
            foreach (var account in accountList)
            {
                account.IsEnabled = false;
                account.UserId = null;
                await accounts.UpdateAsync(account);
            }

            user.IsActive = false;
            await users.UpdateAsync(user);
            return true;
        });

        return new OpResult(true);
    }

    public async Task<OpResult> AssignRolesAsync(long userId, IReadOnlyList<long> roleIds)
    {
        if (await _users.GetByIdAsync(userId) is null)
        {
            return new OpResult(false, "用户不存在");
        }

        await _unitOfWork.UseTranAsync(async () =>
        {
            var userRoles = _unitOfWork.GetRepository<UserRole>();
            await userRoles.DeleteAsync(ur => ur.UserId == userId);
            var links = roleIds.Distinct().Select(roleId => new UserRole { Id = _idGenerator.NextId(), UserId = userId, RoleId = roleId }).ToList();
            if (links.Count > 0)
            {
                await userRoles.InsertRangeAsync(links);
            }

            return true;
        });

        return new OpResult(true);
    }

    // ---------- 账号 ----------

    public async Task<CreateAccountResult> CreateAccountAsync(long userId, string userName, string password)
    {
        if (await _users.GetByIdAsync(userId) is null)
        {
            return new CreateAccountResult(false, "用户不存在");
        }

        if (await _accounts.IsAnyAsync(a => a.UserName == userName))
        {
            return new CreateAccountResult(false, $"登录名 {userName} 已存在");
        }

        var account = new Account
        {
            Id = _idGenerator.NextId(),
            UserName = userName,
            PasswordHash = _passwordHasher.Hash(password),
            UserId = userId,
            IsEnabled = true
        };
        await _accounts.InsertAsync(account);
        return new CreateAccountResult(true, null, account.Id);
    }

    public async Task<OpResult> ResetPasswordAsync(long accountId, string newPassword)
    {
        var account = await _accounts.GetByIdAsync(accountId);
        if (account is null)
        {
            return new OpResult(false, "账号不存在");
        }

        account.PasswordHash = _passwordHasher.Hash(newPassword);
        account.FailedLoginAttempts = 0;
        account.LockedUntil = null;
        await _accounts.UpdateAsync(account);
        return new OpResult(true);
    }

    // ---------- 角色与权限 ----------

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync()
    {
        var list = await _roles.GetListAsync(r => r.IsActive);
        return list.Select(ToDto).ToList();
    }

    public async Task<long> CreateRoleAsync(RoleDto dto)
    {
        if (await _roles.IsAnyAsync(r => r.Code == dto.Code))
        {
            throw new InvalidOperationException($"角色编码 {dto.Code} 已存在");
        }

        var role = new Role
        {
            Id = _idGenerator.NextId(),
            Code = dto.Code,
            Name = dto.Name,
            DataScope = dto.DataScope,
            IsSystem = false,
            IsActive = dto.IsActive
        };
        await _roles.InsertAsync(role);
        return role.Id;
    }

    public async Task<OpResult> UpdateRoleAsync(RoleDto dto)
    {
        if (dto.Id is not { } id)
        {
            return new OpResult(false, "缺少角色 ID");
        }

        var role = await _roles.GetByIdAsync(id);
        if (role is null)
        {
            return new OpResult(false, "角色不存在");
        }

        if (role.IsSystem && role.Code != dto.Code)
        {
            return new OpResult(false, "预置角色编码不可修改");
        }

        role.Code = dto.Code;
        role.Name = dto.Name;
        role.DataScope = dto.DataScope;
        role.IsActive = dto.IsActive;
        await _roles.UpdateAsync(role);
        return new OpResult(true);
    }

    public async Task<OpResult> DeleteRoleAsync(long id)
    {
        var role = await _roles.GetByIdAsync(id);
        if (role is null)
        {
            return new OpResult(false, "角色不存在");
        }

        if (role.IsSystem)
        {
            return new OpResult(false, "预置角色不可删除");
        }

        await _unitOfWork.UseTranAsync(async () =>
        {
            var rolePermissions = _unitOfWork.GetRepository<RolePermission>();
            var userRoles = _unitOfWork.GetRepository<UserRole>();
            var roles = _unitOfWork.GetRepository<Role>();
            await rolePermissions.DeleteAsync(rp => rp.RoleId == id);
            await userRoles.DeleteAsync(ur => ur.RoleId == id);
            await roles.DeleteByIdAsync(id);
            return true;
        });

        return new OpResult(true);
    }

    public async Task<OpResult> SetRolePermissionsAsync(long roleId, IReadOnlyList<long> permissionIds)
    {
        if (await _roles.GetByIdAsync(roleId) is null)
        {
            return new OpResult(false, "角色不存在");
        }

        await _unitOfWork.UseTranAsync(async () =>
        {
            var rolePermissions = _unitOfWork.GetRepository<RolePermission>();
            await rolePermissions.DeleteAsync(rp => rp.RoleId == roleId);
            var links = permissionIds.Distinct().Select(pid => new RolePermission { Id = _idGenerator.NextId(), RoleId = roleId, PermissionId = pid }).ToList();
            if (links.Count > 0)
            {
                await rolePermissions.InsertRangeAsync(links);
            }

            return true;
        });

        return new OpResult(true);
    }

    public async Task<IReadOnlyList<PermissionDto>> GetPermissionCatalogAsync()
    {
        var list = await _permissions.GetListAsync(p => p.IsActive);
        return list.Select(p => new PermissionDto(p.Id, p.Code, p.Name, p.Module)).ToList();
    }

    private static DeptDto ToDto(Dept d) => new(d.Id, d.Code, d.Name, d.ParentId, d.SortOrder, d.IsActive);

    private static UserDto ToDto(User u) => new(u.Id, u.UserNo, u.Name, u.DeptId, u.IsActive);

    private static RoleDto ToDto(Role r) => new(r.Id, r.Code, r.Name, r.DataScope, r.IsSystem, r.IsActive);
}
