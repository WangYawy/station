using System.Linq.Expressions;
using Station.Data.Repositories;

namespace Station.Data.SqlSugar.Repositories;

/// <summary>
/// 批量操作仓储默认实现（基于长连接 SqlSugar 客户端，用于后台长任务）。
/// </summary>
public class LoopRepositoryBase<T> : ILoopRepository<T> where T : class, new()
{
    private readonly ILoopSqlSugarClient _db;

    public LoopRepositoryBase(ILoopSqlSugarClient db)
    {
        _db = db;
    }

    #region 同步

    /// <inheritdoc />
    public T? GetById(long id) => _db.Queryable<T>().In(id).First();

    /// <inheritdoc />
    public T? First(Expression<Func<T, bool>>? predicate = null) => _db.Queryable<T>().First(predicate);

    /// <inheritdoc />
    public List<T> GetList(Expression<Func<T, bool>>? predicate = null)
    {
        var query = _db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return query.ToList();
    }

    /// <inheritdoc />
    public int Count(Expression<Func<T, bool>>? predicate = null)
    {
        var query = _db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return query.Count();
    }

    /// <inheritdoc />
    public int Insert(T entity) => _db.Insertable<T>(entity).ExecuteCommand();

    /// <inheritdoc />
    public int InsertRange(IEnumerable<T> entities) => _db.Insertable<T>(entities).ExecuteCommand();

    /// <inheritdoc />
    public int Update(T entity) => _db.Updateable(entity).ExecuteCommand();

    /// <inheritdoc />
    public int UpdateRange(IEnumerable<T> entities) =>
        _db.Updateable(entities.ToList()).ExecuteCommand();

    /// <inheritdoc />
    public int DeleteById(long id) => _db.Deleteable<T>().In(id).ExecuteCommand();

    /// <inheritdoc />
    public int Delete(Expression<Func<T, bool>> predicate) =>
         _db.Deleteable<T>().Where(predicate).ExecuteCommand();

    /// <inheritdoc />
    public bool IsAny(Expression<Func<T, bool>> predicate) =>
        _db.Queryable<T>().Where(predicate).Any();

    #endregion

    #region 异步

    /// <inheritdoc />
    public async Task<T?> GetByIdAsync(long id) => await _db.Queryable<T>().In(id).FirstAsync();

    /// <inheritdoc />
    public async Task<T?> FirstAsync(Expression<Func<T, bool>> predicate) =>
        await _db.Queryable<T>().Where(predicate).FirstAsync();

    /// <inheritdoc />
    public async Task<List<T>> GetListAsync(Expression<Func<T, bool>>? predicate = null)
    {
        var query = _db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.ToListAsync();
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null)
    {
        var query = _db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.CountAsync();
    }

    /// <inheritdoc />
    public Task<bool> IsAnyAsync(Expression<Func<T, bool>> predicate) =>
        _db.Queryable<T>().Where(predicate).AnyAsync();

    /// <inheritdoc />
    public Task<int> InsertAsync(T entity) => _db.Insertable(entity).ExecuteCommandAsync();

    /// <inheritdoc />
    public Task<int> InsertRangeAsync(IEnumerable<T> entities) =>
        _db.Insertable(entities.ToList()).ExecuteCommandAsync();

    /// <inheritdoc />
    public Task<int> UpdateAsync(T entity) => _db.Updateable(entity).ExecuteCommandAsync();

    /// <inheritdoc />
    public Task<int> UpdateRangeAsync(IEnumerable<T> entities) =>
        _db.Updateable(entities.ToList()).ExecuteCommandAsync();

    /// <inheritdoc />
    public Task<int> DeleteByIdAsync(long id) => _db.Deleteable<T>().In(id).ExecuteCommandAsync();

    /// <inheritdoc />
    public Task<int> DeleteAsync(Expression<Func<T, bool>> predicate) =>
        _db.Deleteable<T>().Where(predicate).ExecuteCommandAsync();

    #endregion
}
