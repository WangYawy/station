using Station.Application.Recorders.Commands;
using Station.Application.Users;
using Station.Contracts;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪应用服务（写侧）。
/// 读侧请使用 <c>IRecorderQueryService</c>。
/// </summary>
public interface IRecorderService
{
    /// <summary>登记记录仪，返回新 Id。</summary>
    Task<long> RegisterAsync(
        string serialNumber, string code, string name, string model, ProtocolType protocol, bool isAuthorized,
        CancellationToken ct = default);

    /// <summary>更新记录仪基本信息、绑定、授权、启用状态。</summary>
    Task<OpResult> UpdateAsync(UpdateRecorderCommand command, CancellationToken ct = default);

    /// <summary>绑定记录仪到用户/部门，并写入 station_bind.ini。</summary>
    Task WriteBindingAsync(
        string serialNumber, long? userId, long? deptId,
        string recorderRootPath, CancellationToken ct = default);

    /// <summary>解绑：清除台账绑定 + 删除 station_bind.ini。</summary>
    Task UnbindAsync(string serialNumber, string recorderRootPath, CancellationToken ct = default);
}
