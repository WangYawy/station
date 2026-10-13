using Station.Application.Accounts;
using Station.Application.Departments;
using Station.Application.Security;
using Station.Application.Users.Commands;
using Station.Data.IdGeneration;
using Station.Data.Repositories;
using Station.Domain.Accounts;
using Station.Domain.Users;

namespace Station.Application.Users;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IUserRoleRepository _userRoles;
    private readonly IAccountRepository _accounts;

    private readonly IUserQueryService _userQuery;
    private readonly IDeptQueryService _deptQuery;
    private readonly IAccountQueryService _accountQuery;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordService _passwordService;
    private readonly IIdGenerator _idGenerator;

    public UserService(
        IUserRepository users,
        IUserRoleRepository userRoles,
        IAccountRepository accounts,
        IUserQueryService userQuery,
        IDeptQueryService deptQuery,
        IAccountQueryService accountQuery,
        IUnitOfWork unitOfWork,
        IPasswordService passwordService,
        IIdGenerator idGenerator)
    {
        _users = users;
        _userRoles = userRoles;
        _accounts = accounts;
        _userQuery = userQuery;
        _deptQuery = deptQuery;
        _accountQuery = accountQuery;
        _unitOfWork = unitOfWork;
        _passwordService = passwordService;
        _idGenerator = idGenerator;
    }

    public async Task<OpResult> CreateAsync(
        CreateUserCommand command, CancellationToken ct = default)
    {
        if (await _userQuery.ExistsByUserNoAsync(command.UserNo, ct))
            return new OpResult(false, $"工号 {command.UserNo} 已存在");

        if (!await _deptQuery.ExistsAsync(command.DeptId, ct))
            return new OpResult(false, "部门不存在");

        if (command.UserName is not null
            && await _accountQuery.ExistsUserNameAsync(command.UserName, ct))
            return new OpResult(false, $"登录名 {command.UserName} 已存在");

        // 事务外预算哈希（CPU 密集）
        string? passwordHash = null;
        if (command.UserName is not null && command.Password is not null)
            passwordHash = await _passwordService.HashAsync(command.Password, ct);

        var now = DateTime.Now;
        var user = User.Create(
            _idGenerator.NewId(), command.UserNo, command.Name, command.DeptId, now);
        if (!command.IsActive) user.Deactivate(now);

        Account? account = null;
        if (command.UserName is not null && passwordHash is not null)
        {
            account = Account.Create(
                _idGenerator.NewId(), command.UserName, passwordHash, user.Id, now);
        }

        await _unitOfWork.UseTranAsync(async () =>
        {
            await _users.AddAsync(user, ct);
            if (account is not null) await _accounts.AddAsync(account, ct);
            return true;
        });

        return new OpResult(true);
    }

    public async Task<OpResult> UpdateAsync(
        UpdateUserCommand command, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(command.Id, ct);
        if (user is null) return new OpResult(false, "用户不存在");

        if (!await _deptQuery.ExistsAsync(command.DeptId, ct))
            return new OpResult(false, "部门不存在");

        var now = DateTime.Now;
        user.UpdateBasicInfo(command.UserNo, command.Name, now);
        if (user.DeptId != command.DeptId) user.ChangeDept(command.DeptId, now);
        if (command.IsActive) user.Activate(now); else user.Deactivate(now);

        await _users.UpdateAsync(user, ct);
        return new OpResult(true);
    }

    public async Task<OpResult> DeleteAsync(long id, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return new OpResult(false, "用户不存在");

        await _unitOfWork.UseTranAsync(async () =>
        {
            await _userRoles.DeleteByUserIdAsync(id, ct);

            var now = DateTime.Now;
            var accountList = await _accounts.GetByUserIdAsync(id, ct);
            foreach (var account in accountList)
            {
                account.Disable(now);
                account.BindUser(null, now);
                await _accounts.UpdateAsync(account, ct);
            }

            user.Deactivate(now);
            await _users.UpdateAsync(user, ct);
            return true;
        });

        return new OpResult(true);
    }

    public async Task<OpResult> AssignRolesAsync(
        AssignUserRolesCommand command, CancellationToken ct = default)
    {
        if (!await _userQuery.ExistsAsync(command.UserId, ct))
            return new OpResult(false, "用户不存在");

        await _unitOfWork.UseTranAsync(async () =>
        {
            await _userRoles.DeleteByUserIdAsync(command.UserId, ct);
            await _userRoles.AddRangeAsync(command.UserId, command.RoleIds, ct);
            return true;
        });

        return new OpResult(true);
    }
}
