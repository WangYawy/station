namespace Station.Application.Permissions.Dtos;

public sealed class PermissionPageQuery
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public string? Keyword { get; set; }
    public string? Module { get; set; }
    public bool? IsActive { get; set; }
}
