
namespace Station.Domain.Repositories;

/// <summary>通用分页查询结果</summary>
public sealed class PageResult<T>
{
    /// <summary>当前页数据</summary>
    public IReadOnlyList<T> Items { get; init; } = [];
    /// <summary>总记录数</summary>
    public int Total { get; init; }
    /// <summary>当前页码</summary>
    public int PageIndex { get; init; } = 1;
    /// <summary>每页条数</summary>
    public int PageSize { get; init; } = AppConst.DefaultPageSize;
    /// <summary>总页数</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;

    /// <summary>是否还有上一页（PageIndex > 1）</summary>
    public bool HasPrevious => PageIndex > 1;

    /// <summary>是否还有下一页（PageIndex 小于 TotalPages）</summary>
    public bool HasNext => PageIndex < TotalPages;
}
