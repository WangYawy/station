using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>
/// 站→平台上报本地队列（断网缓存补报）：注册/文件元数据/报警/指令回执 等。
/// 上报成功标记 Sent，失败保留重试（RetryCount / NextRetryAt 退避）。
/// </summary>
[SugarTable("station_sync_outbox")]
[SugarIndex("idx_outbox_status_next", nameof(Status), OrderByType.Asc, nameof(NextRetryAt), OrderByType.Asc)]
public sealed class SyncOutbox
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    /// <summary>业务主题：register / file-metadata / alert / command-result / config-sync。</summary>
    [SugarColumn(Length = 32)]
    public string Topic { get; set; } = string.Empty;

    /// <summary>上报载荷 JSON。</summary>
    [SugarColumn(Length = 2048)]
    public string PayloadJson { get; set; } = string.Empty;

    public int Status { get; set; } // 0 Pending / 1 Sent / 2 Failed

    public int RetryCount { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? NextRetryAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? SentAt { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; }
}
