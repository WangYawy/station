using System.Linq.Expressions;
using SqlSugar;

namespace Station.Domain.Repositories;

/// <summary>通用分页查询结果</summary>
public class PageResult<T>
{
    /// <summary>当前页数据</summary>
    public IReadOnlyList<T> Items { get; init; } = [];
    /// <summary>总记录数</summary>
    public int Total { get; init; }
    /// <summary>当前页码</summary>
    public int PageIndex { get; init; } = 1;
    /// <summary>每页条数</summary>
    public int PageSize { get; init; } = 20;
    /// <summary>总页数</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;

    /// <summary>是否还有上一页（PageIndex &gt; 1）</summary>
    public bool HasPrevious => PageIndex > 1;

    /// <summary>是否还有下一页（PageIndex &lt; TotalPages）</summary>
    public bool HasNext => PageIndex < TotalPages;

}


/// <summary>
/// 通用仓储接口：覆盖业务模块最常见的 CRUD + 分页场景。
/// </summary>
public interface IRepository<T> where T : class, new()
{
    // ISugarQueryable<T> AsQueryable();

    Task<T?> GetByIdAsync(long id);

    Task<T?> FirstAsync(Expression<Func<T, bool>> predicate);

    Task<List<T>> GetListAsync(Expression<Func<T, bool>>? predicate = null);

    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null);

    Task<bool> IsAnyAsync(Expression<Func<T, bool>> predicate);

    Task<int> InsertAsync(T entity);

    Task<int> InsertRangeAsync(IEnumerable<T> entities);

    Task<int> UpdateAsync(T entity);

    Task<int> UpdateRangeAsync(IEnumerable<T> entities);

    Task<int> DeleteByIdAsync(long id);

    Task<int> DeleteAsync(Expression<Func<T, bool>> predicate);

    Task<int> SoftDeleteByIdAsync(long id);

    Task<int> SoftDeleteAsync(Expression<Func<T, bool>> predicate);

    Task<PageResult<T>> ToPageAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        OrderByType orderType = OrderByType.Asc);
}
