using SqlSugar;
using Station.Domain;
using Station.Domain.Repositories;
using Station.Infrastructure.Repositories;

namespace Station.Infrastructure;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ISqlSugarClient _db;
    private bool _finished;

    public UnitOfWork(ISqlSugarClient db)
    {
        _db = db;
    }

    public IRepository<T> GetRepository<T>() where T : class, new() => new RepositoryBase<T>(_db);

    public void Begin() => _db.Ado.BeginTran();

    public void Commit()
    {
        _db.Ado.CommitTran();
        _finished = true;
    }

    public void Rollback()
    {
        _db.Ado.RollbackTran();
        _finished = true;
    }

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
