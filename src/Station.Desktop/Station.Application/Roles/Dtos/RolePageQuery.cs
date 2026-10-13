namespace Station.Application.Roles.Dtos;

public sealed class RolePageQuery
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>按编码/名称模糊匹配。</summary>
    public string? Keyword { get; set; }

    public bool? IsActive { get; set; }
    public bool? IsSystem { get; set; }
}
