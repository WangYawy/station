using System.Linq.Expressions;
using SqlSugar;
using Station.Domain.Repositories;
using Station.Infrastructure.Db;

namespace Station.Infrastructure.Repositories;

public class LoopRepositoryBase<T> : ILoopRepository<T> where T : class, new()
{
    private readonly ILoopSqlSugarClient _db; // 注入的是长连接标记接口（Scoped）
    private bool _disposed;

    public LoopRepositoryBase(ILoopSqlSugarClient db)
    {
        _db = db;
    }

    public ISugarQueryable<T> AsQueryable() => _db.Queryable<T>();

    #region 同步
    public T? GetById(long id) => _db.Queryable<T>().In(id).First();

    public T? First(Expression<Func<T, bool>>? predicate = null) => _db.Queryable<T>().First(predicate);

    public List<T> GetList(Expression<Func<T, bool>>? predicate = null)
    {
        var query = _db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return query.ToList();
    }

    public int Count(Expression<Func<T, bool>>? predicate = null)
    {
        var query = _db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return query.Count();
    }

    public int Insert(T entity) => _db.Insertable<T>(entity).ExecuteCommand();
    public int InsertRange(IEnumerable<T> entities) => _db.Insertable<T>(entities).ExecuteCommand();

    public int Update(T entity) => _db.Updateable(entity).ExecuteCommand();

    public int UpdateRange(IEnumerable<T> entities) =>
        _db.Updateable(entities.ToList()).ExecuteCommand();

    public int DeleteById(long id) => _db.Deleteable<T>().In(id).ExecuteCommand();

    public int Delete(Expression<Func<T, bool>> predicate) =>
         _db.Deleteable<T>().Where(predicate).ExecuteCommand();

    public bool IsAny(Expression<Func<T, bool>> predicate) =>
        _db.Queryable<T>().Where(predicate).Any();
    #endregion
    #region 异步
    public async Task<T?> GetByIdAsync(long id) => await _db.Queryable<T>().In(id).FirstAsync();

    public async Task<T?> FirstAsync(Expression<Func<T, bool>> predicate) =>
        await _db.Queryable<T>().Where(predicate).FirstAsync();

    public async Task<List<T>> GetListAsync(Expression<Func<T, bool>>? predicate = null)
    {
        var query = _db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.ToListAsync();
    }

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null)
    {
        var query = _db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.CountAsync();
    }

    public Task<bool> IsAnyAsync(Expression<Func<T, bool>> predicate) =>
        _db.Queryable<T>().Where(predicate).AnyAsync();

    public Task<int> InsertAsync(T entity) => _db.Insertable(entity).ExecuteCommandAsync();

    public Task<int> InsertRangeAsync(IEnumerable<T> entities) =>
        _db.Insertable(entities.ToList()).ExecuteCommandAsync();

    public Task<int> UpdateAsync(T entity) => _db.Updateable(entity).ExecuteCommandAsync();

    public Task<int> UpdateRangeAsync(IEnumerable<T> entities) =>
        _db.Updateable(entities.ToList()).ExecuteCommandAsync();

    public Task<int> DeleteByIdAsync(long id) => _db.Deleteable<T>().In(id).ExecuteCommandAsync();

    public Task<int> DeleteAsync(Expression<Func<T, bool>> predicate) =>
        _db.Deleteable<T>().Where(predicate).ExecuteCommandAsync();
    #endregion
}
