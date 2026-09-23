namespace Station.Crypto.Abstractions;

/// <summary>对称消息认证码（MAC）。</summary>
public interface IMacProvider
{
    /// <summary>算法标识。</summary>
    string Algorithm { get; }

    /// <summary>计算 MAC。</summary>
    string Compute(byte[] key, byte[] data);

    /// <summary>验证 MAC（固定时间比较）。</summary>
    bool Verify(byte[] key, byte[] data, string macBase64);
}
