namespace Station.Device.Runtime.FileSystem;

/// <summary>
/// 扫描过滤内部辅助。
/// 无状态、纯静态、不依赖配置或注入，供各 IDeviceFileSystem 实现共享。
/// </summary>
internal static class ScanFilter
{
    /// <summary>
    /// 扩展名白名单判定（含点、不区分大小写）。null/空集合 = 允许全部。
    /// </summary>
    public static bool IsExtensionAllowed(
        string fileName, IReadOnlyCollection<string>? extensions)
    {
        if (extensions is null || extensions.Count == 0) return true;

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return false;

        foreach (var e in extensions)
        {
            if (string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// 完整相对路径判定（Include 优先 / Exclude 阻断）。
    /// </summary>
    public static bool IsPathAllowed(
        string relativePath,
        IReadOnlyCollection<string>? includePathPrefixes,
        IReadOnlyCollection<string>? excludePathPrefixes)
    {
        var normalized = Normalize(relativePath);

        if (includePathPrefixes is { Count: > 0 })
        {
            var matched = false;
            foreach (var p in includePathPrefixes)
            {
                if (IsUnderPrefix(normalized, p)) { matched = true; break; }
            }
            if (!matched) return false;
        }

        if (excludePathPrefixes is { Count: > 0 })
        {
            foreach (var p in excludePathPrefixes)
            {
                if (IsUnderPrefix(normalized, p)) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 目录是否值得继续下钻（剪枝）。
    /// 返回 false 表示该目录不可能通向任何 Include 前缀，直接跳过。
    /// </summary>
    public static bool ShouldDescend(
        string currentRelativePath,
        IReadOnlyCollection<string>? includePathPrefixes)
    {
        if (includePathPrefixes is null || includePathPrefixes.Count == 0) return true;

        var current = Normalize(currentRelativePath);
        if (current.Length == 0) return true;

        foreach (var raw in includePathPrefixes)
        {
            var prefix = Normalize(raw);
            if (prefix.Length == 0) return true;

            // 当前目录是目标前缀的祖先（继续下钻能找到目标）
            if (string.Equals(prefix, current, StringComparison.OrdinalIgnoreCase)) return true;
            if (prefix.StartsWith(current + "/", StringComparison.OrdinalIgnoreCase)) return true;

            // 当前目录是目标前缀的后代（已在目标内）
            if (current.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    // ---------- 内部 ----------

    private static string Normalize(string? path)
        => (path ?? string.Empty).Replace('\\', '/').Trim('/');

    private static bool IsUnderPrefix(string normalizedPath, string rawPrefix)
    {
        var prefix = Normalize(rawPrefix);
        if (prefix.Length == 0) return true;
        return string.Equals(normalizedPath, prefix, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
