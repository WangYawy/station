using System.Linq.Expressions;
using Station.Data.Paging;

namespace Station.Data.Repositories;

/// <summary>
/// 通用仓储接口：覆盖业务模块最常见的 CRUD + 分页场景。<br/>
/// <b>注意：本接口不暴露 SqlSugar 类型，Application 层可安全依赖。</b><br/>
/// 若确需 SqlSugar 原生能力，请参考 <c>Station.Data.SqlSugar.Extensions.RepositoryExtensions</c>（逃生舱）<br/>
/// 或在 Infrastructure 层编写专用查询服务。
/// </summary>
public interface IRepository<T> where T : class, new()
{
    /// <summary>按主键查询。</summary>
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>按条件查询首条。</summary>
    Task<T?> FirstAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>按条件查询列表。</summary>
    Task<List<T>> GetListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    /// <summary>按条件统计数量。</summary>
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    /// <summary>是否存在满足条件的记录。</summary>
    Task<bool> IsAnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>插入单条。</summary>
    Task<int> InsertAsync(T entity, CancellationToken ct = default);

    /// <summary>批量插入。</summary>
    Task<int> InsertRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    /// <summary>更新单条（按主键）。</summary>
    Task<int> UpdateAsync(T entity, CancellationToken ct = default);

    /// <summary>批量更新（按主键）。</summary>
    Task<int> UpdateRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    /// <summary>按主键删除。</summary>
    Task<int> DeleteByIdAsync(long id, CancellationToken ct = default);

    /// <summary>按条件删除。</summary>
    Task<int> DeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>按主键软删除。</summary>
    Task<int> SoftDeleteByIdAsync(long id, CancellationToken ct = default);

    /// <summary>按条件软删除。</summary>
    Task<int> SoftDeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>
    /// 单字段排序分页（最常用）。<br/>
    /// 例：<c>repo.ToPageAsync(query, x =&gt; x.CreatedAt, descending: true, ct)</c>
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
