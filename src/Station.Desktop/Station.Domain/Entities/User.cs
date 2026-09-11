using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>
/// 用户（人员）：记录仪持有者、文件归属主体。工号为跨端自然键。
/// 区别于登录账号（Account）。
/// </summary>
[SugarTable("station_user")]
[SugarIndex("uk_user_userno", nameof(UserNo), OrderByType.Asc, true)]
public sealed class User
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 32)]
    public string UserNo { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = string.Empty;

    public long DeptId { get; set; }

    public bool IsActive { get; set; } = true;
}
