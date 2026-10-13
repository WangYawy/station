namespace Station.Application.Accounts.Dtos;

public sealed class AccountPageQuery
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public string? Keyword { get; set; }
    public bool? IsEnabled { get; set; }

    /// <summary>是否只看锁定中。</summary>
    public bool? IsLocked { get; set; }

    /// <summary>限定关联用户。</summary>
    public long? UserId { get; set; }
}
