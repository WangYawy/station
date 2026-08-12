using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>用户-角色 关联（一人可多角色，取最宽数据范围）。</summary>
[SugarTable("station_user_role")]
[SugarIndex("uk_user_role", nameof(UserId), OrderByType.Asc, nameof(RoleId), OrderByType.Asc, true)]
public sealed class UserRole
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long UserId { get; set; }

    public long RoleId { get; set; }
}
