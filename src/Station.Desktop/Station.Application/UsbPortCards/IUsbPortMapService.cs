namespace Station.Application.UsbPortCards;

/// <summary>物理端口 → 1-30 序号映射表读写与热加载。</summary>
public interface IUsbPortMapService
{
    /// <summary>物理端口 ID → 1-30 序号。</summary>
    IReadOnlyDictionary<string, int> GetMapping();

    /// <summary>新增或更新单条映射。</summary>
    Task SetMappingAsync(string physicalPortId, int displayPortNo, CancellationToken ct = default);

    /// <summary>删除单条映射。</summary>
    Task RemoveMappingAsync(string physicalPortId, CancellationToken ct = default);

    /// <summary>导出为 JSON 文本。</summary>
    string ExportJson();

    /// <summary>从 JSON 文本导入并热加载。</summary>
    Task ImportJsonAsync(string json, CancellationToken ct = default);

    /// <summary>持久化到 appsettings.json。</summary>
    Task SaveAsync(CancellationToken ct = default);
}
