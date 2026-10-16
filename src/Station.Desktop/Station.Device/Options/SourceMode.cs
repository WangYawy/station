namespace Station.Device.Options;

/// <summary>设备检测源模式。</summary>
public enum SourceMode
{
    /// <summary>仅检测 UMS（可移动磁盘）。</summary>
    Ums = 0,

    /// <summary>仅检测 MTP（便携设备）。</summary>
    Mtp = 1,

    /// <summary>UMS + MTP 同时检测。</summary>
    Mixed = 2,

    /// <summary>模拟源（开发调试，不检测真实设备）。</summary>
    Simulated = 3
}
