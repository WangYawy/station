using System.Linq.Expressions;

namespace Station.Data.Repositories;

/// <summary>
/// 批量操作仓储（同步，用于后台长任务，内部使用长连接）。
/// </summary>
public interface ILoopRepository<T> where T : class, new()
{
    #region 同步方法

    /// <summary>按主键查询。</summary>
    T? GetById(long id);

    /// <summary>按条件查询首条。</summary>
    T? First(Expression<Func<T, bool>>? predicate = null);

    /// <summary>按条件查询列表。</summary>
    List<T> GetList(Expression<Func<T, bool>>? predicate = null);

    /// <summary>按条件统计数量。</summary>
    int Count(Expression<Func<T, bool>>? predicate = null);

    /// <summary>插入单条。</summary>
    int Insert(T entity);

    /// <summary>批量插入。</summary>
    int InsertRange(IEnumerable<T> entities);

    /// <summary>更新单条。</summary>
    int Update(T entity);

    /// <summary>批量更新。</summary>
    int UpdateRange(IEnumerable<T> entities);

    /// <summary>按主键删除。</summary>
    int DeleteById(long id);

    /// <summary>按条件删除。</summary>
    int Delete(Expression<Func<T, bool>> predicate);

    /// <summary>是否存在满足条件的记录。</summary>
    bool IsAny(Expression<Func<T, bool>> predicate);

    #endregion

    #region 异步

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

    #endregion
}
