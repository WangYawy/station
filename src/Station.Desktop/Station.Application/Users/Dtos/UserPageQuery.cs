namespace Station.Application.Users.Dtos;

public sealed class UserPageQuery
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>按工号/姓名模糊匹配。</summary>
    public string? Keyword { get; set; }

    public bool? IsActive { get; set; }

    /// <summary>限定部门（null 表示不筛选）。</summary>
    public long? DeptId { get; set; }

    /// <summary>是否包含子部门（需与 DeptId 搭配）。</summary>
    public bool IncludeChildDepts { get; set; }
}
