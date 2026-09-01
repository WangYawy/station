using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Station.Domain.Repositories;
/// <summary>
/// 批量操作仓储（同步，用于后台长任务，内部使用长连接）
/// </summary>
public interface ILoopRepository<T> where T : class, new()
{
    #region 同步方法
    T? GetById(long id);
    T? First(Expression<Func<T, bool>>? predicate = null);
    List<T> GetList(Expression<Func<T, bool>>? predicate = null);
    int Count(Expression<Func<T, bool>>? predicate = null);
    int Insert(T entity);
    int InsertRange(IEnumerable<T> entities);
    int Update(T entity);
    int UpdateRange(IEnumerable<T> entities);
    int DeleteById(long id);
    int Delete(Expression<Func<T, bool>> predicate);
    bool IsAny(Expression<Func<T, bool>> predicate);
    #endregion
    // 按需添加其他同步方法
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
