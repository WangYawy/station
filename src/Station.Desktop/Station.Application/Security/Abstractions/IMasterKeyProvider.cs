namespace Station.Application.Security.Abstractions;

/// <summary>
/// 主密钥提供者：从密钥文件（默认 %ProgramData%/Station/keys/master.key）
/// 或环境变量 STATION_MASTER_KEY 读取。支持版本化轮换。
/// </summary>
public interface IMasterKeyProvider
{
    /// <summary>当前活动密钥版本（写入用）。</summary>
    int CurrentVersion { get; }

    /// <summary>按版本获取 32 字节密钥。版本不存在时抛异常。</summary>
    byte[] GetKey(int version);

    /// <summary>生成新版本密钥并标记为当前版本。返回新版本号。</summary>
    Task<int> RotateAsync(CancellationToken ct = default);

    /// <summary>获取所有可用版本号（升序）。</summary>
    IReadOnlyList<int> GetVersions();
}
