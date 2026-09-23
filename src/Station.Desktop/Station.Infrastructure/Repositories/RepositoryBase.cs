using System.Linq.Expressions;
using SqlSugar;
using Station.Domain;
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

    public async Task<T?> GetByIdAsync(long id, CancellationToken ct = default) => await Db.Queryable<T>().In(id).FirstAsync(ct);

    public async Task<T?> FirstAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await Db.Queryable<T>().Where(predicate).FirstAsync(ct);

    public async Task<List<T>> GetListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = Db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.ToListAsync(ct);
    }

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = Db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.CountAsync(ct);
    }

    public Task<bool> IsAnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Db.Queryable<T>().Where(predicate).AnyAsync(ct);

    public Task<int> InsertAsync(T entity, CancellationToken ct = default) => Db.Insertable(entity).ExecuteCommandAsync(ct);

    public Task<int> InsertRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) =>
        Db.Insertable(entities.ToList()).ExecuteCommandAsync(ct);

    public Task<int> UpdateAsync(T entity, CancellationToken ct = default) => Db.Updateable(entity).ExecuteCommandAsync(ct);

    public Task<int> UpdateRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) =>
        Db.Updateable(entities.ToList()).ExecuteCommandAsync(ct);

    public Task<int> DeleteByIdAsync(long id, CancellationToken ct = default) => Db.Deleteable<T>().In(id).ExecuteCommandAsync(ct);

    public Task<int> DeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Db.Deleteable<T>().Where(predicate).ExecuteCommandAsync(ct);

    public Task<int> SoftDeleteByIdAsync(long id, CancellationToken ct = default) => Db.Deleteable<T>().In(id).ExecuteCommandAsync(ct);

    public Task<int> SoftDeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Db.Updateable<T>().Where(predicate).ExecuteCommandAsync(ct);

    public Task<PageResult<T>> ToPageAsync<TKey>(
         PageQuery<T> query,
         Expression<Func<T, TKey>> orderBy,
         bool descending = false,
         CancellationToken ct = default)
    {
        var sort = descending
            ? SortDescriptor<T>.Desc(orderBy)
            : SortDescriptor<T>.Asc(orderBy);
        return ToPageAsync(query, [sort], ct);
    }

    public async Task<PageResult<T>> ToPageAsync(
        PageQuery<T> query,
        IReadOnlyList<SortDescriptor<T>> sorts,
        CancellationToken ct = default)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, AppConst.MaxPageSize);

        var q = Db.Queryable<T>();
        if (query.Predicate is not null)
        {
            q = q.Where(query.Predicate);
        }

        // SqlSugar 的 OrderBy 是"追加"语义，多次调用即多级排序
        foreach (var sort in sorts)
        {
            q = q.OrderBy(sort.KeySelector,
                          sort.Descending ? OrderByType.Desc : OrderByType.Asc);
        }

        if (query.CountTotal)
        {
            RefAsync<int> total = 0;
            var items = await q.ToPageListAsync(pageIndex, pageSize, total);
            return new PageResult<T>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        // 跳过 COUNT：Total 用 -1 表示"未统计"
        var sliced = await q.Skip((pageIndex - 1) * pageSize)
                            .Take(pageSize)
                            .ToListAsync();
        return new PageResult<T>
        {
            Items = sliced,
            Total = -1,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }
}
