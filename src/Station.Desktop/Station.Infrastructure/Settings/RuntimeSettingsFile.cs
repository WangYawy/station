using Station.Application.Settings;
using Station.Infrastructure;

namespace Station.Desktop.Infrastructure.Settings;

/// <summary>
/// 运行时设置文件：<c>appsettings.runtime.json</c>，与应用同目录，配置加载顺序在 appsettings.json 之后。
/// </summary>
public sealed class RuntimeSettingsFile : IRuntimeSettingsFile
{
    public const string FileName = "appsettings.runtime.json";

    /// <summary>环境变量可覆盖运行时文件路径（测试/便携部署用）。</summary>
    public const string PathEnvName = "STATION__RUNTIMESETTINGSPATH";

    public static string ResolvePath() =>
        Environment.GetEnvironmentVariable(PathEnvName) is { Length: > 0 } overridePath
            ? overridePath
            : System.IO.Path.Combine(StationPaths.DataDirectory, FileName);

    private static string Path => ResolvePath();

    public string? ReadJson() => File.Exists(Path) ? File.ReadAllText(Path) : null;

    public void WriteJson(string json)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, json);
    }
}
