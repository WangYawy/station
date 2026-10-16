using SqlSugar;

namespace Station.Infrastructure.Persistence.Users;

[SugarTable("station_user")]
[SugarIndex("uk_user_userno", nameof(UserNo), OrderByType.Asc, true)]
public sealed class UserPo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 32)]
    public string UserNo { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = string.Empty;

    public long DeptId { get; set; }
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public long? CreatedBy { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public long? UpdatedBy { get; set; }
}
