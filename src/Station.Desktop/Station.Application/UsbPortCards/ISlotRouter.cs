namespace Station.Application.UsbPortCards;

/// <summary>卡片槽位路由：设备/任务 → 槽位（0-based）。单例，无状态路由。</summary>
public interface ISlotRouter
{
    SlotMode Mode { get; }
    int Capacity { get; }

    /// <summary>为设备分配槽位；未映射返回 null（Fixed 模式下 UI 显示 #??）。</summary>
    int? ResolveSlotForDevice(string deviceKey);

    /// <summary>为任务查槽位；无绑定返回 null。</summary>
    int? ResolveSlotForTask(long taskId);

    /// <summary>显式绑定任务 → 槽位。</summary>
    void BindTask(long taskId, int slot);

    /// <summary>释放槽位（设备断开或卡片归零后调用）。</summary>
    void ReleaseSlot(int slot);

    /// <summary>清空所有映射（测试 / 重启用）。</summary>
    void Reset();

    /// <summary>当前绑定快照（发现模式用）。Key=SlotIndex，Value=DeviceKey。</summary>
    IReadOnlyDictionary<int, string> GetSlotBindings();
}
