namespace Station.Application.Departments.Dtos;

public sealed class DeptPageQuery
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>按编码/名称模糊匹配。</summary>
    public string? Keyword { get; set; }

    /// <summary>为 null 表示不筛选。</summary>
    public bool? IsActive { get; set; }

    /// <summary>限定父级（null 表示不筛选；0 表示根节点）。</summary>
    public long? ParentId { get; set; }
}
