namespace Station.Contracts.Api;

/// <summary>分页结果（平台统一列表展示）。</summary>
public sealed record PagedResult<T>(int PageIndex, int PageSize, long TotalCount, IReadOnlyList<T> Items);
