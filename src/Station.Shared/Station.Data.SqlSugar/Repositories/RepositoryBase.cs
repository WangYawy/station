using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Data.Paging;
using Station.Data.Repositories;

namespace Station.Data.SqlSugar.Repositories;

/// <summary>
/// 通用仓储默认实现（基于 SqlSugar 短连接客户端）。
/// </summary>
public class RepositoryBase<T> : IRepository<T> where T : class, new()
{
    /// <summary>SqlSugar 客户端（短连接）。派生类可访问。</summary>
    protected ISqlSugarClient Db { get; }

    /// <summary>供同程序集扩展方法（如 <c>RepositoryExtensions.AsQueryable</c>）使用。</summary>
    internal ISqlSugarClient DbContext => Db;

    private readonly PagingOptions _pagingOptions;

    public RepositoryBase(ISqlSugarClient db, IOptions<PagingOptions> pagingOptions)
    {
        Db = db;
        _pagingOptions = pagingOptions.Value;
    }

    /// <inheritdoc />
    public async Task<T?> GetByIdAsync(long id, CancellationToken ct = default)
        => await Db.Queryable<T>().In(id).FirstAsync(ct);

    /// <inheritdoc />
    public async Task<T?> FirstAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await Db.Queryable<T>().Where(predicate).FirstAsync(ct);

    /// <inheritdoc />
    public async Task<List<T>> GetListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = Db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = Db.Queryable<T>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.CountAsync(ct);
    }

    /// <inheritdoc />
    public Task<bool> IsAnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Db.Queryable<T>().Where(predicate).AnyAsync(ct);

    /// <inheritdoc />
    public Task<int> InsertAsync(T entity, CancellationToken ct = default)
        => Db.Insertable(entity).ExecuteCommandAsync(ct);

    /// <inheritdoc />
    public Task<int> InsertRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
        => Db.Insertable(entities.ToList()).ExecuteCommandAsync(ct);

    /// <inheritdoc />
    public Task<int> UpdateAsync(T entity, CancellationToken ct = default)
        => Db.Updateable(entity).ExecuteCommandAsync(ct);

    /// <inheritdoc />
    public Task<int> UpdateRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
        => Db.Updateable(entities.ToList()).ExecuteCommandAsync(ct);

    /// <inheritdoc />
    public Task<int> DeleteByIdAsync(long id, CancellationToken ct = default)
        => Db.Deleteable<T>().In(id).ExecuteCommandAsync(ct);

    /// <inheritdoc />
    public Task<int> DeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Db.Deleteable<T>().Where(predicate).ExecuteCommandAsync(ct);

    /// <inheritdoc />
    public Task<int> SoftDeleteByIdAsync(long id, CancellationToken ct = default)
        => Db.Deleteable<T>().In(id).ExecuteCommandAsync(ct);

    /// <inheritdoc />
    public Task<int> SoftDeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Db.Updateable<T>().Where(predicate).ExecuteCommandAsync(ct);

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<PageResult<T>> ToPageAsync(
        PageQuery<T> query,
        IReadOnlyList<SortDescriptor<T>> sorts,
        CancellationToken ct = default)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, _pagingOptions.MaxPageSize);

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
                            .ToListAsync(ct);
        return new PageResult<T>
        {
            Items = sliced,
            Total = -1,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }
}
