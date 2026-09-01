using System.Linq.Expressions;
using SqlSugar;
using Station.Domain.Repositories;

namespace Station.Infrastructure.Repositories;

public class RepositoryBase<T> : IRepository<T> where T : class, new()
{
    protected ISqlSugarClient Db { get; }

    public RepositoryBase(ISqlSugarClient db)
    {
        Db = db;
    }

    public ISugarQueryable<T> AsQueryable() => Db.Queryable<T>();

    public async Task<T?> GetByIdAsync(long id) => await Db.Queryable<T>().In(id).FirstAsync();

    public async Task<T?> FirstAsync(Expression<Func<T, bool>> predicate) =>
        await Db.Queryable<T>().Where(predicate).FirstAsync();

    public async Task<List<T>> GetListAsync(Expression<Func<T, bool>>? predicate = null)
    {
        var query = Db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.ToListAsync();
    }

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null)
    {
        var query = Db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.CountAsync();
    }

    public Task<bool> IsAnyAsync(Expression<Func<T, bool>> predicate) =>
        Db.Queryable<T>().Where(predicate).AnyAsync();

    public Task<int> InsertAsync(T entity) => Db.Insertable(entity).ExecuteCommandAsync();

    public Task<int> InsertRangeAsync(IEnumerable<T> entities) =>
        Db.Insertable(entities.ToList()).ExecuteCommandAsync();

    public Task<int> UpdateAsync(T entity) => Db.Updateable(entity).ExecuteCommandAsync();

    public Task<int> UpdateRangeAsync(IEnumerable<T> entities) =>
        Db.Updateable(entities.ToList()).ExecuteCommandAsync();

    public Task<int> DeleteByIdAsync(long id) => Db.Deleteable<T>().In(id).ExecuteCommandAsync();

    public Task<int> DeleteAsync(Expression<Func<T, bool>> predicate) =>
        Db.Deleteable<T>().Where(predicate).ExecuteCommandAsync();

    public Task<int> SoftDeleteByIdAsync(long id) => Db.Deleteable<T>().In(id).ExecuteCommandAsync();

    public Task<int> SoftDeleteAsync(Expression<Func<T, bool>> predicate) =>
        Db.Updateable<T>().Where(predicate).ExecuteCommandAsync();

    public async Task<PageResult<T>> ToPageAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        OrderByType orderType = OrderByType.Asc)
    {
        var query = Db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        if (orderBy is not null)
        {
            query = query.OrderBy(orderBy, orderType);
        }

        var total = new RefAsync<int>();
        var items = await query.ToPageListAsync(pageIndex, pageSize, total);
        return new PageResult<T>(total.Value, items);
    }
}
