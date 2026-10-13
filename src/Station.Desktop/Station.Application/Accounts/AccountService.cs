using Station.Application.Accounts.Commands;
using Station.Application.Security;
using Station.Application.Users;
using Station.Data.IdGeneration;
using Station.Domain.Accounts;

namespace Station.Application.Accounts;

public sealed class AccountService : IAccountService
{
    private readonly IAccountRepository _accounts;
    private readonly IUserQueryService _userQuery;
    private readonly IAccountQueryService _accountQuery;
    private readonly IPasswordService _passwordService;
    private readonly IIdGenerator _idGenerator;

    public AccountService(
        IAccountRepository accounts,
        IUserQueryService userQuery,
        IAccountQueryService accountQuery,
        IPasswordService passwordService,
        IIdGenerator idGenerator)
    {
        _accounts = accounts;
        _userQuery = userQuery;
        _accountQuery = accountQuery;
        _passwordService = passwordService;
        _idGenerator = idGenerator;
    }

    public async Task<CreateAccountResult> CreateAsync(
        CreateAccountCommand command, CancellationToken ct = default)
    {
        if (!await _userQuery.ExistsAsync(command.UserId, ct))
            return new CreateAccountResult(false, "用户不存在");

        if (await _accountQuery.ExistsUserNameAsync(command.UserName, ct))
            return new CreateAccountResult(false, $"登录名 {command.UserName} 已存在");

        var passwordHash = await _passwordService.HashAsync(command.Password, ct);
        var account = Account.Create(
            _idGenerator.NewId(), command.UserName, passwordHash,
            command.UserId, DateTime.Now);

        await _accounts.AddAsync(account, ct);
        return new CreateAccountResult(true, null, account.Id);
    }

    public async Task<OpResult> ResetPasswordAsync(
        ResetPasswordCommand command, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(command.AccountId, ct);
        if (account is null) return new OpResult(false, "账号不存在");

        var now = DateTime.Now;
        var newHash = await _passwordService.HashAsync(command.NewPassword, ct);

        account.ChangePassword(newHash, now);
        account.RecordLoginSuccess(now);

        await _accounts.UpdateAsync(account, ct);
        return new OpResult(true);
    }
}
