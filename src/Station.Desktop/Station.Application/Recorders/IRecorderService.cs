using Station.Application.Recorders.Commands;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪台账应用服务。
/// 
/// 【职责】
///   - 台账 CRUD（新增 / 修改 / 删除）
///   - 状态切换（激活 / 停用）
///   - 绑定 / 解绑（含写 station_bind.ini）
/// 
/// 【写读分离】
///   - 写：走 IRecorderRepository（短连接 UoW）
///   - 读：走 IRecorderQueryService（DTO 直投影）
/// 
/// 【绑定语义】
///   - 绑定时记录仪必须在线（需要写设备内绑定文件）；
///   - 写文件成功后更新台账 BoundUserId / DeptId；
///   - 解绑时删除设备内绑定文件（设备在线时）并清空台账。
/// </summary>
public interface IRecorderService
{
    // ========== CRUD ==========

    /// <summary>新增记录仪台账，返回新 ID。</summary>
    Task<long> CreateAsync(CreateRecorderCommand cmd, CancellationToken ct = default);

    /// <summary>修改记录仪基础信息（不含序列号、不写绑定文件）。</summary>
    Task UpdateAsync(UpdateRecorderCommand cmd, CancellationToken ct = default);

    /// <summary>删除记录仪台账（已绑定 / 有采集任务的需先解绑）。</summary>
    Task DeleteAsync(long id, CancellationToken ct = default);

    // ========== 状态 ==========

    /// <summary>激活记录仪。</summary>
    Task ActivateAsync(long id, CancellationToken ct = default);

    /// <summary>停用记录仪（停用后识别会被拒绝）。</summary>
    Task DeactivateAsync(long id, CancellationToken ct = default);

    // ========== 绑定 / 解绑 ==========

    /// <summary>
    /// 绑定记录仪到用户 / 部门。
    /// 内部流程：校验台账 → 校验用户/部门 → 匹配在线设备 → 写 station_bind.ini → 更新台账。
    /// </summary>
    Task<BindResult> BindAsync(BindRecorderCommand cmd, CancellationToken ct = default);

    /// <summary>
    /// 解绑记录仪。
    /// 内部流程：清空台账 BoundUserId / DeptId → 若设备在线则删除 station_bind.ini。
    /// </summary>
    Task<UnbindResult> UnbindAsync(long recorderId, CancellationToken ct = default);
}
