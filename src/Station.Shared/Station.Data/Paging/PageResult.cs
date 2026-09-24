namespace Station.Data.Paging;

/// <summary>通用分页查询结果。</summary>
public sealed class PageResult<T>
{
    /// <summary>当前页数据。</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>总记录数；<c>-1</c> 表示"未统计"。</summary>
    public int Total { get; init; }

    /// <summary>当前页码。</summary>
    public int PageIndex { get; init; } = 1;

    /// <summary>每页条数。</summary>
    public int PageSize { get; init; } = PagingDefaults.DefaultPageSize;

    /// <summary>总页数；<see cref="Total"/> 为 -1 时返回 0。</summary>
    public int TotalPages => PageSize > 0 && Total >= 0
        ? (int)Math.Ceiling((double)Total / PageSize)
        : 0;

    /// <summary>是否还有上一页（PageIndex &gt; 1）。</summary>
    public bool HasPrevious => PageIndex > 1;

    /// <summary>是否还有下一页（PageIndex 小于 TotalPages）。</summary>
    public bool HasNext => Total >= 0 && PageIndex < TotalPages;
}
