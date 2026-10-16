using SqlSugar;

namespace Station.Infrastructure.Persistence.Outbox;

[SugarTable("station_sync_outbox")]
[SugarIndex("idx_outbox_status_next", nameof(Status), OrderByType.Asc, nameof(NextRetryAt), OrderByType.Asc)]
public sealed class SyncOutboxPo
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(Length = 32)] public string Topic { get; set; } = "";
    [SugarColumn(Length = 2048)] public string PayloadJson { get; set; } = "";
    public int Status { get; set; }
    public int RetryCount { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? NextRetryAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? SentAt { get; set; }
    [SugarColumn(Length = 512, IsNullable = true)] public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
}
