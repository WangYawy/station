using System.Linq.Expressions;
using SqlSugar;

namespace Station.Domain.Repositories;

/// <summary>分页结果。</summary>
public sealed record PageResult<T>(int Total, List<T> Items);

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
