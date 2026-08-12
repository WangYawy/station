using Station.Contracts;

namespace Station.Domain.Entities;

/// <summary>记录仪（含白名单）。序列号为跨端自然键。</summary>
public sealed class Recorder
{
    public long Id { get; set; }

    public required string SerialNumber { get; set; }

    public required string Model { get; set; }

    public ProtocolType Protocol { get; set; }

    /// <summary>绑定用户（人员）ID。</summary>
    public long? BoundUserId { get; set; }

    public long? DeptId { get; set; }

    public bool IsAuthorized { get; set; }
}
