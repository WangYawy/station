using Station.Domain.Repositories;

namespace Station.Domain;

/// <summary>
/// 工作单元：持有独立 SqlSugar 客户端，保证事务内所有仓储操作同库同事务。
/// 用法：Begin → 多次仓储操作 → Commit；异常时 Rollback（或依赖 Dispose 自动回滚）。
/// </summary>
public interface IUnitOfWork : IDisposable
{
    IRepository<T> GetRepository<T>() where T : class, new();

    void Begin();

    void Commit();

    void Rollback();

    TResult UseTran<TResult>(Func<TResult> action);

    Task<TResult> UseTranAsync<TResult>(Func<Task<TResult>> action);
}
