using Station.Contracts;

namespace Station.Application.Recorders.Dtos;

public sealed class RecorderListItemDto
{
    public long Id { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public ProtocolType Protocol { get; set; }
    public long? BoundUserId { get; set; }
    public string? BoundUserName { get; set; }
    public long? DeptId { get; set; }
    public string? DeptName { get; set; }
    public bool IsAuthorized { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
