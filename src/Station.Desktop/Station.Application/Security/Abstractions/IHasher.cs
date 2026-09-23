namespace Station.Application.Security.Abstractions;

/// <summary>单向摘要算法抽象（SM3 / SHA-256）。</summary>
public interface IHasher
{
    /// <summary>算法标识。</summary>
    string Algorithm { get; }

    /// <summary>计算摘要，返回 Base64。</summary>
    string ComputeHash(byte[] data);

    /// <summary>流式计算摘要（大文件用）。</summary>
    Task<string> ComputeHashAsync(Stream stream, CancellationToken ct = default);
}
