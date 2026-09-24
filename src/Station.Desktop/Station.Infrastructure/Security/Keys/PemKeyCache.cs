using System.Collections.Concurrent;

namespace Station.Infrastructure.Security.Keys;

/// <summary>
/// PEM 密钥文件读取缓存（internal）。
/// 相对路径基于应用目录解析，绝对路径直接读。
/// </summary>
public sealed class PemKeyCache
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly string _baseDir;

    public PemKeyCache(string? baseDir = null)
        => _baseDir = baseDir ?? AppContext.BaseDirectory;

    /// <summary>同步读（缓存命中直接返回）。</summary>
    public string Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (_cache.TryGetValue(path, out var cached)) return cached;

        var full = ResolveFullPath(path);
        if (!File.Exists(full)) return string.Empty;

        var content = File.ReadAllText(full);
        _cache[path] = content;
        return content;
    }

    /// <summary>异步读。</summary>
    public async Task<string> GetAsync(string? path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (_cache.TryGetValue(path, out var cached)) return cached;

        var full = ResolveFullPath(path);
        if (!File.Exists(full)) return string.Empty;

        var content = await File.ReadAllTextAsync(full, ct).ConfigureAwait(false);
        _cache[path] = content;
        return content;
    }

    /// <summary>清空缓存（密钥文件更新后调用）。</summary>
    public void Clear() => _cache.Clear();

    private string ResolveFullPath(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(_baseDir, path);
}
