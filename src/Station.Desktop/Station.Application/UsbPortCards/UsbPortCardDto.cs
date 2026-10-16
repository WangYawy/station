using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Application.UsbPortCards;

/// <summary>
/// 工作台卡片快照 DTO。
/// 
/// 【设计要点】
///   - SlotIndex：0-based，与 ISlotRouter 一致（不是 1-based！）
///   - DisplayPortNo：1-30 显示号，来自 IUsbPortMapService；0 表示未映射
///   - PhysicalPortId：物理端口标识（Windows: Win:Hub0001:Port0003）
///   - LinkState：物理层快照，只会是 Offline / Verifying
///                 业务态（Bound / Rejected）由 UI 事件驱动，快照不携带
///   - Version：UI 版本号，用于卡片内部仲裁，防止旧快照覆盖新事件
/// </summary>
public sealed record UsbPortCardDto
{
    /// <summary>卡槽索引（0-based，与 ISlotRouter 一致）</summary>
    public int SlotIndex { get; init; }

    /// <summary>物理端口显示号（1-30）；0 表示未映射</summary>
    public int DisplayPortNo { get; init; }

    /// <summary>物理端口标识（如 "WIN:HUB0001:PORT0003"）；null 表示未解析</summary>
    public string? PhysicalPortId { get; init; }

    /// <summary>设备稳定 Key（如 "PORT:WIN:HUB0001:PORT0003"）</summary>
    public string? DeviceKey { get; init; }

    /// <summary>设备名称（记录仪友好名）</summary>
    public string? DeviceName { get; init; }

    /// <summary>协议类型（UMS / MTP / PrivateSdk）</summary>
    public ProtocolType? Protocol { get; init; }

    /// <summary>物理设备是否在线</summary>
    public bool IsConnected { get; init; }

    /// <summary>任务操作人用户 ID。</summary>
    public long? OperatorUserId { get; init; }

    /// <summary>任务操作人姓名（展示用）。</summary>
    public string? OperatorName { get; init; }

    /// <summary>任务操作人所属部门名（展示用）。</summary>
    public string? OperatorDeptName { get; init; }

    /// <summary>
    /// 链路态（物理层快照）。
    /// 只会是 Offline / Verifying；Bound / Rejected 由 UI 层从业务事件获得。
    /// </summary>
    public DeviceLinkState LinkState { get; init; } = DeviceLinkState.Offline;

    /// <summary>设备可用空间（字节）；null 表示未知</summary>
    public long? DeviceAvailableBytes { get; init; }

    /// <summary>设备总空间（字节）；null 表示未知</summary>
    public long? DeviceTotalBytes { get; init; }

    /// <summary>关联的任务 ID；null 表示无任务</summary>
    public long? TaskId { get; init; }

    /// <summary>任务编号</summary>
    public string? TaskNo { get; init; }

    /// <summary>任务状态</summary>
    public CollectTaskStatus? Status { get; init; }

    /// <summary>总文件数</summary>
    public int TotalFiles { get; init; }

    /// <summary>已采集文件数</summary>
    public int CollectedFiles { get; init; }

    /// <summary>总字节数</summary>
    public long TotalBytes { get; init; }

    /// <summary>已采集字节数</summary>
    public long CollectedBytes { get; init; }

    /// <summary>采集速度（字节/秒）</summary>
    public double SpeedBytesPerSecond { get; init; }

    /// <summary>是否紧急优先</summary>
    public bool IsEmergency { get; init; }

    /// <summary>任务开始时间</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// UI 版本号。
    /// 由 CollectTaskService.NextUiVersion 递增，用于卡片内部仲裁（丢弃旧数据）。
    /// </summary>
    public long Version { get; init; }
}
