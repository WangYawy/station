namespace Station.Device.Runtime.FileSystem;

/// <summary>
/// MTP 路径工具：判断 / 拼接 MTP 根路径。
/// </summary>
public static class MtpRoot
{
    /// <summary>MTP 根路径前缀（内部约定，非系统路径）。</summary>
    public const string Prefix = "mtp://";

    /// <summary>判断是否为 MTP 根路径。</summary>
    public static bool IsMtpRoot(string? root)
        => !string.IsNullOrWhiteSpace(root)
           && root.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>规范化 MTP 根（去尾部斜杠）。</summary>
    public static string Normalize(string root)
        => root.TrimEnd('/', '\\');
}
