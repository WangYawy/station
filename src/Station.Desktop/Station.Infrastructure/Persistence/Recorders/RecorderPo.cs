using SqlSugar;
using Station.Contracts;

namespace Station.Infrastructure.Persistence.Recorders;

[SugarTable("station_recorder")]
[SugarIndex("uk_recorder_serial", nameof(SerialNumber), OrderByType.Asc, true)]
public sealed class RecorderPo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string SerialNumber { get; set; } = string.Empty;
    [SugarColumn(Length = 64)]
    public string Code { get; set; } = string.Empty;
    [SugarColumn(Length = 64)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Model { get; set; } = string.Empty;

    public ProtocolType Protocol { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? BoundUserId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    public bool IsAuthorized { get; set; }
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public long? CreatedBy { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public long? UpdatedBy { get; set; }
}
