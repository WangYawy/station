using Station.Domain.Accounts;

namespace Station.Infrastructure.Persistence.Accounts;

internal static class AccountMapper
{
    public static Account ToDomain(AccountPo po)
        => Account.Restore(
            po.Id,
            po.UserName,
            po.PasswordHash,
            po.UserId,
            po.IsEnabled,
            po.FailedLoginAttempts,
            po.LockedUntil,
            po.CreatedAt,
            po.CreatedBy,
            po.UpdatedAt,
            po.UpdatedBy,
            po.IsDeleted,
            po.DeletedAt,
            po.DeletedBy);

    public static AccountPo ToPo(Account a)
        => new()
        {
            Id = a.Id,
            UserName = a.UserName,
            PasswordHash = a.PasswordHash,
            UserId = a.UserId,
            IsEnabled = a.IsEnabled,
            FailedLoginAttempts = a.FailedLoginAttempts,
            LockedUntil = a.LockedUntil,
            CreatedAt = a.CreatedAt,
            CreatedBy = a.CreatedBy,
            UpdatedAt = a.UpdatedAt,
            UpdatedBy = a.UpdatedBy,
            IsDeleted = a.IsDeleted,
            DeletedAt = a.DeletedAt,
            DeletedBy = a.DeletedBy
        };
}
