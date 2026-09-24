using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Data.Paging;
using Station.Data.SqlSugar.Repositories;

namespace Station.Data.Repositories;

/// <summary>
/// 工作单元实现：使用独立 SqlSugar 客户端保证事务隔离。
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ISqlSugarClient _db;
    private readonly IOptions<PagingOptions> _pagingOptions;
    private bool _finished;

    public UnitOfWork(ISqlSugarClient db, IOptions<PagingOptions> pagingOptions)
    {
        _db = db;
        _pagingOptions = pagingOptions;
    }

    /// <inheritdoc />
    public IRepository<T> GetRepository<T>() where T : class, new()
        => new RepositoryBase<T>(_db, _pagingOptions);

    /// <inheritdoc />
    public void Begin() => _db.Ado.BeginTran();

    /// <inheritdoc />
    public void Commit()
    {
        _db.Ado.CommitTran();
        _finished = true;
    }

    /// <inheritdoc />
    public void Rollback()
    {
        _db.Ado.RollbackTran();
        _finished = true;
    }

    /// <inheritdoc />
    public TResult UseTran<TResult>(Func<TResult> action)
    {
        Begin();
        try
        {
            var result = action();
            Commit();
            return result;
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<TResult> UseTranAsync<TResult>(Func<Task<TResult>> action)
    {
        Begin();
        try
        {
            var result = await action();
            Commit();
            return result;
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_finished)
        {
            try
            {
                _db.Ado.RollbackTran();
            }
            catch
            {
                // 无活动事务时忽略
            }
        }

        _db.Dispose();
    }
}
