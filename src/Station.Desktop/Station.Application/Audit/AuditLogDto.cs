namespace Station.Application.Audit;

/// <summary>日志中心列表项。</summary>
public sealed record AuditLogDto
{
    /// <summary>格式化后的时间，如 2026-09-17 14:03:21。</summary>
    public required string TimeText { get; init; }

    /// <summary>操作人（姓名缺失时回退账号，再缺失为 system）。</summary>
    public required string Operator { get; init; }

    /// <summary>操作类型 Code，如 login / backup.auto。</summary>
    public required string Type { get; init; }

    /// <summary>操作类型显示名，如 登录 / 自动备份；未登记 code 原样回退。</summary>
    public required string TypeText { get; init; }

    /// <summary>操作对象。</summary>
    public required string Target { get; init; }

    /// <summary>详情。</summary>
    public required string Detail { get; init; }

    /// <summary>结果文本：成功 / 失败。</summary>
    public required string ResultText { get; init; }

    /// <summary>结果颜色（#RRGGBB），由 Desktop 层转换为画刷。</summary>
    public required string ResultColor { get; init; }
}
