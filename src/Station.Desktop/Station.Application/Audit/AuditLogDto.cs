// 说明：Service 层完成全部格式化（时间文本、结果文本、结果颜色），
//       ViewModel 与 View 直接使用，不再做二次包装。

namespace Station.Application.Audit;

/// <summary>日志中心列表项。</summary>
public sealed record AuditLogDto
{
    /// <summary>格式化后的时间，如 2026-09-17 14:03:21。</summary>
    public required string TimeText { get; init; }

    /// <summary>操作人（姓名缺失时回退账号，再缺失为 system）。</summary>
    public required string Operator { get; init; }

    /// <summary>操作类型，如 Login / ConfigChange / Erase / Import / Export。</summary>
    public required string Type { get; init; }

    /// <summary>操作对象。</summary>
    public required string Target { get; init; }

    /// <summary>详情。</summary>
    public required string Detail { get; init; }

    /// <summary>结果文本：成功 / 失败。</summary>
    public required string ResultText { get; init; }

    /// <summary>结果颜色（#RRGGBB），由 Desktop 层转换为画刷。</summary>
    public required string ResultColor { get; init; }
}
