using System.Collections.Concurrent;
using Station.Application.Security.Abstractions;

namespace Station.Infrastructure.Security.Keys;

/// <summary>从相对/绝对路径读取 PEM 密钥文件。缓存到内存避免重复 IO。</summary>
public sealed class KeyFileResolver : IKeyFileResolver
{
    private readonly string _baseDir;
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public KeyFileResolver(string? baseDir = null)
        => _baseDir = baseDir ?? AppContext.BaseDirectory;

    public async Task<string> ResolveAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (_cache.TryGetValue(path, out var cached)) return cached;

        var full = Path.IsPathRooted(path) ? path : Path.Combine(_baseDir, path);
        if (!File.Exists(full)) return string.Empty;

        var content = await File.ReadAllTextAsync(full, ct).ConfigureAwait(false);
        _cache[path] = content;
        return content;
    }

    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (_cache.TryGetValue(path, out var cached)) return cached;

        var full = Path.IsPathRooted(path) ? path : Path.Combine(_baseDir, path);
        if (!File.Exists(full)) return string.Empty;

        var content = File.ReadAllText(full);
        _cache[path] = content;
        return content;
    }
}
