namespace Station.Contracts.Reporting;

/// <summary>上报结果（Duplicate 表示命中唯一索引，平台侧幂等处理）。</summary>
public sealed record ReportResult(bool Accepted, bool Duplicate, string? ErrorCode = null);
