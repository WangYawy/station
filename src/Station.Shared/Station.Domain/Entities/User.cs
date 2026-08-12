namespace Station.Domain.Entities;

/// <summary>
/// 用户（人员）：记录仪持有者、文件归属主体。
/// 工号为跨端自然键；区别于登录账号（Account）。
/// </summary>
public sealed class User
{
    public long Id { get; set; }

    public required string UserNo { get; set; }

    public required string Name { get; set; }

    public long DeptId { get; set; }

    public bool IsActive { get; set; } = true;
}
