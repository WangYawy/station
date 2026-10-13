using SqlSugar;
using Station.Data.SoftDelete;

namespace Station.Infrastructure.Persistence.Accounts;

[SugarTable("station_account")]
[SugarIndex("uk_account_username", nameof(UserName), OrderByType.Asc, true)]
public sealed class AccountPo : ISoftDelete
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = string.Empty;

    [SugarColumn(Length = 256)]
    public string PasswordHash { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public long? UserId { get; set; }

    public bool IsEnabled { get; set; }

    public int FailedLoginAttempts { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LockedUntil { get; set; }

    // 审计
    public DateTime CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }

    // 软删除
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public long? DeletedBy { get; set; }
}
