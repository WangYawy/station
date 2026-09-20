using System.Linq.Expressions;
using SqlSugar;

namespace Station.Domain.Repositories;



/// <summary>
/// 通用仓储接口：覆盖业务模块最常见的 CRUD + 分页场景。
/// </summary>
public interface IRepository<T> where T : class, new()
{
    ISugarQueryable<T> AsQueryable();

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

    /// <summary>
    /// 单字段排序分页（最常用）。<br/>
    /// 例：<c>repo.ToPageAsync(query, x => x.CreatedAt, descending: true, ct)</c>
    /// </summary>
    Task<PageResult<T>> ToPageAsync<TKey>(
        PageQuery<T> query,
        Expression<Func<T, TKey>> orderBy,
        bool descending = false,
        CancellationToken ct = default);

    /// <summary>
    /// 多字段排序分页。<br/>
    /// 例：<c>repo.ToPageAsync(query,
    ///     [SortDescriptor&lt;Log&gt;.Desc(x =&gt; x.CreatedAt),
    ///      SortDescriptor&lt;Log&gt;.Asc(x =&gt; x.Id)], ct)</c>
    /// </summary>
    Task<PageResult<T>> ToPageAsync(
        PageQuery<T> query,
        IReadOnlyList<SortDescriptor<T>> sorts,
        CancellationToken ct = default);
}
