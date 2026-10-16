using Microsoft.Extensions.Logging;
using Station.Application.Security;
using Station.Crypto;
using Station.Device.Detection;
using Station.Device.FileSystem;

namespace Station.Application.Recorders;

/// <summary>
/// 绑定文件业务实现：序列化 + MAC 计算 + 验签。
/// 文件 IO 全部委托给 <see cref="IDeviceFileSystem"/>，本类不感知协议差异。
/// </summary>
public sealed class RecorderBindingFile : IRecorderBindingFile
{
    private const string FileName = BindingFileCodec.FileName;

    private readonly IDeviceFileSystemProvider _fileSystems;
    private readonly ICryptoPolicyService _cryptoPolicy;
    private readonly ICryptoAlgorithmRegistry _algorithmRegistry;
    private readonly ILogger<RecorderBindingFile> _logger;

    public RecorderBindingFile(
        IDeviceFileSystemProvider fileSystems,
        ICryptoPolicyService cryptoPolicy,
        ICryptoAlgorithmRegistry algorithmRegistry,
        ILogger<RecorderBindingFile> logger)
    {
        _fileSystems = fileSystems;
        _cryptoPolicy = cryptoPolicy;
        _algorithmRegistry = algorithmRegistry;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        DetectedDevice device, BindingInfo binding, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(binding);

        // 1. 策略：取 MAC 算法
        var macProvider = await _cryptoPolicy
            .GetMacProviderAsync(CryptoUsage.RecorderBinding, ct)
            .ConfigureAwait(false);

        // 2. 密钥：从主密钥 HKDF 派生
        var key = await _cryptoPolicy
            .GetBindingMacKeyAsync(ct)
            .ConfigureAwait(false);

        // 3. 协议：序列化 + MAC
        var mac = BindingFileCodec.ComputeMac(binding, key, macProvider);
        var content = BindingFileCodec.Serialize(binding, macProvider.Algorithm, mac);

        // 4. IO：按协议走 Device 层
        var fs = _fileSystems.GetFor(device.Protocol);
        fs.WriteAllText(device, FileName, content);

        _logger.LogDebug("绑定文件已写入：{Root}（MAC 算法 {Algo}）",
            device.Root, macProvider.Algorithm);
    }

    /// <inheritdoc />
    public BindingInfo? Read(DetectedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var fs = _fileSystems.GetFor(device.Protocol);
        var content = fs.ReadAllText(device, FileName);
        if (content is null) return null;

        var info = BindingFileCodec.Parse(content);
        if (info is null)
            _logger.LogWarning("绑定文件格式错误：{Root}", device.Root);

        return info;
    }

    /// <inheritdoc />
    public async Task<bool> VerifySignatureAsync(
        DetectedDevice device, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        // 1. IO：读原始内容
        var fs = _fileSystems.GetFor(device.Protocol);
        var content = fs.ReadAllText(device, FileName);
        if (content is null) return false;

        // 2. 解析
        var info = BindingFileCodec.Parse(content);
        if (info is null || string.IsNullOrEmpty(info.Signature))
            return false;

        // 3. 密钥
        var key = await _cryptoPolicy
            .GetBindingMacKeyAsync(ct)
            .ConfigureAwait(false);

        // 4. 从 INI 里读 mac_algo → 解析 MAC 提供者
        var macAlgo = BindingFileCodec.GetMacAlgo(content) ?? CryptoAlgorithm.HmacSm3;

        IMacProvider macProvider;
        try
        {
            macProvider = _algorithmRegistry.GetMacProvider(macAlgo);
        }
        catch (NotSupportedException)
        {
            _logger.LogWarning("绑定文件声明的 MAC 算法未注册：{Algo}", macAlgo);
            return false;
        }

        // 5. 校验（固定时间比较在 Codec 内部实现）
        var ok = BindingFileCodec.VerifyMac(info, key, macProvider);
        if (!ok)
            _logger.LogWarning("绑定文件 MAC 校验失败：{Root}", device.Root);

        return ok;
    }

    /// <inheritdoc />
    public void Delete(DetectedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var fs = _fileSystems.GetFor(device.Protocol);
        fs.DeleteFile(device, FileName);
    }
}
