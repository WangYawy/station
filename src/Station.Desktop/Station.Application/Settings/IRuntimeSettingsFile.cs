namespace Station.Application.Settings;

/// <summary>
/// 运行时设置文件（appsettings.runtime.json）抽象：位于应用目录，加载顺序在 appsettings.json 之后，
/// 用于持久化管理员在设置页/桌面端修改的配置项，重启后生效。
/// </summary>
public interface IRuntimeSettingsFile
{
    /// <summary>读取完整 JSON；文件不存在返回 null。</summary>
    string? ReadJson();

    /// <summary>整体写回 JSON（由调用方保证合并语义）。</summary>
    void WriteJson(string json);
}
