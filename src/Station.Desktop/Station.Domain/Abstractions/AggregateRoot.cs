namespace Station.Domain.Abstractions;

/// <summary>
/// 聚合根基类。持有领域事件队列，由应用服务在持久化后统一发布。
/// 非线程安全：聚合根的生命周期由调用方（应用服务）保证单线程使用。
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _events = new();

    /// <summary>未发布的领域事件（只读视图）。</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    /// <summary>派生类调用，注册一个领域事件。</summary>
    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _events.Add(domainEvent);
    }

    /// <summary>发布后由应用服务清空。</summary>
    public void ClearDomainEvents() => _events.Clear();
}
