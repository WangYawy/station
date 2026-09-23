namespace Station.Application.Security.Abstractions;

/// <summary>从相对/绝对路径读取 PEM 密钥文件。缓存到内存避免重复 IO。</summary>
public interface IKeyFileResolver
{
    Task<string> ResolveAsync(string path, CancellationToken ct = default);
    string Resolve(string path);
}
