namespace Station.Application.Roles.Dtos;

public sealed class RoleOptionDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DataScope { get; set; }
}
