namespace Station.Application.Permissions.Dtos;

public sealed class PermissionGroupedDto
{
    public string Module { get; set; } = string.Empty;
    public List<PermissionListItemDto> Items { get; set; } = [];
}
