namespace Station.Infrastructure;

/// <summary>
/// 应用数据目录（运行期可写数据统一落点）：
/// Windows <c>%LOCALAPPDATA%\Station</c>，Linux <c>~/.local/share/Station</c>（信创桌面发行版）。
/// 数据库、运行时设置文件（appsettings.runtime.json）等默认放这里，安装目录保持只读。
/// 可用环境变量 <c>STATION__DATA__DIR</c> 覆盖（测试/便携部署）。
/// </summary>
public static class StationPaths
{
    public const string DataDirEnvName = "STATION__DATA__DIR";

    public static string DataDirectory =>
        Environment.GetEnvironmentVariable(DataDirEnvName) is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Station");

    /// <summary>
    /// SQLite 相对连接串重定位到数据目录：<c>Data Source=xxx.db</c> 改为数据目录下的绝对路径；
    /// 绝对路径、<c>:memory:</c>、<c>file:</c> URI 原样保留。
    /// </summary>
    public static string RebaseSqliteConnectionString(string connectionString)
    {
        const string marker = "Data Source=";
        var index = connectionString.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return connectionString;
        }

        var valueStart = index + marker.Length;
        var valueEnd = valueStart;
        while (valueEnd < connectionString.Length && connectionString[valueEnd] != ';')
        {
            valueEnd++;
        }

        var value = connectionString[valueStart..valueEnd].Trim();
        if (value.StartsWith(":memory:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(value))
        {
            return connectionString;
        }

        var rebased = Path.Combine(DataDirectory, value);
        return connectionString[..valueStart] + rebased + connectionString[valueEnd..];
    }
}
