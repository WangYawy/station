using Station.Contracts;

namespace Station.Application.Recorders.Commands;

public sealed class UpdateRecorderCommand
{
    public long Id { get; init; }
    public string Model { get; init; } = string.Empty;
    public ProtocolType Protocol { get; init; }
    public long? BoundUserId { get; init; }
    public long? DeptId { get; init; }
    public bool IsAuthorized { get; init; }
    public bool IsActive { get; init; }
}
