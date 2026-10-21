using Station.Application.Collecting.Dtos;
using Station.Application.Recorders;

namespace Station.Application.Collecting;

/// <summary>
/// 采集任务应用服务。
/// 
/// 【职责】
///   - 任务生命周期管理（创建 / 启动 / 暂停 / 恢复 / 取消 / 中断 / 续采）
///   - 与设备层协作（DetectedDevice 由 TaskDeviceFactory 从聚合根构造）
///   - UI 事件发布（状态变化 / 进度 / 文件完成）
///   - 领域事件发布（触发审计 / 上传 / 平台同步）
/// 
/// 【续采策略】
///   - 新建：Created → Scanning → Collecting
///   - 续采：Interrupted / Failed / Paused → Scanning → Collecting（ResumeCollecting）
///   - 续采时统计字段累加，不清空历史 Completed
/// </summary>
public interface ICollectTaskService
{
    // ========== 创建 / 启动 ==========

    /// <summary>
    /// 用识别后的设备创建采集任务。
    /// 
    /// 【行为】
    ///   - 构造 DeviceRef（不可变设备快照）
    ///   - 落库 CollectTask（Created 状态）
    ///   - 发布领域事件 + UI 状态事件
    ///   - 若 AutoCollectOnConnect=true 则自动 StartAsync
    /// 
    /// 【调用方】ApplicationDeviceEventSink.HandleMatchedAsync（识别通过后）
    /// </summary>
    Task<CollectTaskDto> CreateTaskAsync(
        BoundDevice device, bool isAuto, CancellationToken ct = default);

    /// <summary>
    /// 启动 / 续采任务。
    /// 
    /// 【行为】
    ///   - 授权 + 采集开关校验
    ///   - 扫描设备文件（ScanAsync）
    ///   - 按指纹跳过已完成文件
    ///   - 新建 vs 续采：走 BeginCollecting / ResumeCollecting
    ///   - 启动 LongRunning 后台采集线程
    /// 
    /// 【状态约束】
    ///   - Created / Interrupted / Failed / Paused → Scanning → Collecting
    ///   - 其他状态拒绝启动
    /// </summary>
    Task<CollectTaskDto> StartAsync(long taskId, CancellationToken ct = default);

    // ========== 控制 ==========

    /// <summary>
    /// 暂停任务（发送暂停信号，等待后台线程收尾）。
    /// 
    /// 【行为】
    ///   - 设置 PauseRequested，触发 Cts.Cancel
    ///   - 后台线程 FinalizeAsync 检测到暂停 → 当前文件 ResetToPending
    ///   - 任务状态 → Paused
    /// </summary>
    Task PauseAsync(long taskId, CancellationToken ct = default);

    /// <summary>
    /// 恢复暂停的任务（Paused → Collecting，重启后台采集线程）。
    /// </summary>
    Task ResumeAsync(long taskId, CancellationToken ct = default);

    /// <summary>
    /// 取消任务（Canceled 终态）。
    /// 
    /// 【行为】
    ///   - 有活动控制块：设置 FinalStatus=Canceled，触发 Cts.Cancel，由 FinalizeAsync 落库
    ///   - 无控制块：直接对空闲任务执行 Cancel
    ///   - 未完成文件全部 Cancel
    /// </summary>
    Task CancelAsync(long taskId, CancellationToken ct = default);

    /// <summary>
    /// 中断任务（Interrupted 终态）。
    /// 
    /// 【触发场景】
    ///   - 设备物理断开（ApplicationDeviceEventSink.OnDisconnectedAsync）
    ///   - 启动修复（StartupRepairService 处理崩溃残留）
    /// 
    /// 【行为】同 CancelAsync，但 finalStatus=Interrupted，未完成文件 Interrupt
    /// </summary>
    Task InterruptAsync(long taskId, string reason, CancellationToken ct = default);

    /// <summary>
    /// 设置 / 取消紧急优先标记。
    /// 
    /// 【约束】
    ///   - 设为紧急时校验 MaxEmergencyTasks 上限
    ///   - 超限返回 false，调用方提示用户
    /// </summary>
    Task<bool> SetEmergencyAsync(
        long taskId, bool isEmergency, CancellationToken ct = default);

    // ========== 批量控制 ==========

    /// <summary>
    /// 暂停所有运行中任务（优雅关闭用）。
    /// 
    /// 【行为】
    ///   - 对 _controls 中所有 taskId 发送 PauseAsync
    ///   - 轮询等待 _controls 清空（带超时）
    ///   - 超时未完成则记录告警
    /// 
    /// 【调用方】App.axaml.cs ShutdownRequested
    /// </summary>
    Task PauseAllAsync(TimeSpan timeout, CancellationToken ct = default);

    // ========== 读 ==========

    /// <summary>按 ID 查询任务 DTO。</summary>
    Task<CollectTaskDto?> GetTaskAsync(long taskId, CancellationToken ct = default);

    /// <summary>查询最近 N 个任务（按 Id 倒序）。</summary>
    Task<IReadOnlyList<CollectTaskDto>> GetTasksAsync(
        int count, CancellationToken ct = default);

    /// <summary>查询活跃任务（Created / Scanning / Collecting / Paused）。</summary>
    Task<IReadOnlyList<CollectTaskDto>> GetActiveTasksAsync(CancellationToken ct = default);

    /// <summary>查询任务的全部文件 DTO。</summary>
    Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(
        long taskId, CancellationToken ct = default);

    /// <summary>
    /// 查可续采任务（Paused / Interrupted / Failed）。
    /// 
    /// 【行为】
    ///   - 从最近 100 个任务中过滤
    ///   - physicalPortId 为 null 返回 null
    ///   - 按 PhysicalPortId 大小写不敏感匹配
    /// 
    /// 【调用方】ApplicationDeviceEventSink.HandleMatchedAsync
    ///   - 命中：不建新任务，等用户重试或自动续采
    ///   - 未命中：走 AutoCollectOnConnect 建新任务
    /// </summary>
    Task<CollectTaskDto?> FindResumeableTaskAsync(
        string? physicalPortId, CancellationToken ct = default);

    // ========== UI 版本 ==========

    /// <summary>
    /// 获取任务的 UI 版本号（单调递增）。
    /// 
    /// 【用途】
    ///   - 快照与事件同源版本仲裁
    ///   - UsbPortCardViewModel 丢弃旧数据
    /// </summary>
    long GetUiVersion(long taskId);
}
