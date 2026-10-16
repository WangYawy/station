namespace Station.Application.UsbPortCards;

/// <summary>卡片槽位分配模式。</summary>
public enum SlotMode
{
    /// <summary>固定模式：卡片与物理端口 1:1 绑定，USB1 永远 #01。</summary>
    Fixed = 0,

    /// <summary>空闲模式：卡片池化，谁先接入谁占 #01，拔掉后释放。</summary>
    Pooled = 1
}
