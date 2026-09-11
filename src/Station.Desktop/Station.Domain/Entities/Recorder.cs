using SqlSugar;
using Station.Contracts;

namespace Station.Domain.Entities;

/// <summary>
/// 记录仪台账（含白名单）。序列号为跨端自然键。
/// 归属绑定：BoundUserId / DeptId 与记录仪根目录 ini（station_bind.ini）互为载体。
/// </summary>
[SugarTable("station_recorder")]
[SugarIndex("uk_recorder_serial", nameof(SerialNumber), OrderByType.Asc, true)]
public sealed class Recorder
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string SerialNumber { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Model { get; set; } = string.Empty;

    public ProtocolType Protocol { get; set; }

    /// <summary>绑定的用户（人员）ID。</summary>
    [SugarColumn(IsNullable = true)]
    public long? BoundUserId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    /// <summary>是否在白名单（授权接入）。</summary>
    public bool IsAuthorized { get; set; }

    public bool IsActive { get; set; } = true;
}
