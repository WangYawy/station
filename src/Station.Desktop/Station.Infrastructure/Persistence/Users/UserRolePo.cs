using SqlSugar;

namespace Station.Infrastructure.Persistence.Users;

[SugarTable("station_user_role")]
[SugarIndex("uk_user_role", nameof(UserId), OrderByType.Asc, nameof(RoleId), OrderByType.Asc, true)]
public sealed class UserRolePo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long UserId { get; set; }
    public long RoleId { get; set; }
}
