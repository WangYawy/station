namespace Station.Application.Recorders;

/// <summary>记录仪台账与绑定管理。</summary>
public interface IRecorderService
{
    Task<IReadOnlyList<RecorderDto>> GetRecordersAsync();

    Task<RecorderDto> RegisterAsync(
        string serialNumber,
        string model,
        Station.Contracts.ProtocolType protocol,
        bool isAuthorized);

    Task<RecorderDto> UpdateAsync(RecorderDto dto);

    /// <summary>写绑定：更新台账绑定并写入记录仪根目录 ini（含 SM3 签名）。</summary>
    Task WriteBindingAsync(string serialNumber, long? userId, long? deptId, string recorderRootPath);

    /// <summary>解除绑定：清台账绑定并删除 ini。</summary>
    Task UnbindAsync(string serialNumber, string recorderRootPath);
}
