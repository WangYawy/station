namespace Station.Data.Repositories;

/// <summary>
/// 工作单元：持有独立 客户端，保证事务内所有仓储操作同库同事务。<br/>
/// 用法：Begin → 多次仓储操作 → Commit；异常时 Rollback（或依赖 Dispose 自动回滚）。
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>获取一个绑定当前事务的仓储。</summary>
    IRepository<T> GetRepository<T>() where T : class, new();

    /// <summary>开启事务。</summary>
    void Begin();

    /// <summary>提交事务。</summary>
    void Commit();

    /// <summary>回滚事务。</summary>
    void Rollback();

    /// <summary>事务包裹同步委托（自动 Commit / Rollback）。</summary>
    TResult UseTran<TResult>(Func<TResult> action);

    /// <summary>事务包裹异步委托（自动 Commit / Rollback）。</summary>
    Task<TResult> UseTranAsync<TResult>(Func<Task<TResult>> action);
}
