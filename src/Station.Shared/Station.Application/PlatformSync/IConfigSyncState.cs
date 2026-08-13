namespace Station.Application.PlatformSync;

/// <summary>配置同步状态（进程内）：记录最近一次拉取版本/变更数/时间。</summary>
public interface IConfigSyncState
{
    long? Version { get; }

    int? ChangeCount { get; }

    DateTime? LastSyncAt { get; }

    void Record(long version, int changeCount);
}

public sealed class ConfigSyncState : IConfigSyncState
{
    public long? Version { get; private set; }

    public int? ChangeCount { get; private set; }

    public DateTime? LastSyncAt { get; private set; }

    public void Record(long version, int changeCount)
    {
        Version = version;
        ChangeCount = changeCount;
        LastSyncAt = DateTime.Now;
    }
}
