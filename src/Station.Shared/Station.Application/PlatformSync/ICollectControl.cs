namespace Station.Application.PlatformSync;

/// <summary>采集运行开关（远程指令 StopCollecting / StartCollecting 控制）。</summary>
public interface ICollectControl
{
    bool CollectingEnabled { get; }

    void StopCollecting(string? reason = null);

    void StartCollecting();

    string? StoppedReason { get; }
}

public sealed class CollectControl : ICollectControl
{
    public bool CollectingEnabled { get; private set; } = true;

    public string? StoppedReason { get; private set; }

    public void StopCollecting(string? reason = null)
    {
        CollectingEnabled = false;
        StoppedReason = reason;
    }

    public void StartCollecting()
    {
        CollectingEnabled = true;
        StoppedReason = null;
    }
}
