namespace Station.Application.PlatformSync;

/// <summary>上报队列：入队 + 消费补报。</summary>
public interface ISyncOutboxService
{
    Task<long> EnqueueAsync(string topic, string payloadJson);

    /// <summary>消费待上报（补报），返回成功条数。</summary>
    Task<int> DrainAsync(int maxItems = 50);

    Task<int> CountPendingAsync();
}
