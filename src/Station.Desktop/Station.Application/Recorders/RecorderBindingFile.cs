using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Collecting;
using Station.Application.Security;
using Station.Contracts;
using Station.Crypto;
using Station.Domain.Collecting;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪绑定文件（station_bind.ini）实现。
///
/// 【职责】IO + 策略 + 密钥获取。
/// 【协议】全部交由 <see cref="BindingFileCodec"/> 处理（唯一实现）。
///
/// 【依赖倒置】只依赖 Application 层接口 + Crypto 契约，不引用 Engine / Infrastructure。
/// </summary>
public sealed class RecorderBindingFile : IRecorderBindingFile
{
    private readonly ICollectSourceProvider _sourceProvider;
    private readonly ICryptoPolicyService _cryptoPolicy;
    private readonly ICryptoAlgorithmRegistry _algorithmRegistry;
    private readonly ILogger<RecorderBindingFile> _logger;

    public RecorderBindingFile(
        ICollectSourceProvider sourceProvider,
        ICryptoPolicyService cryptoPolicy,
        ICryptoAlgorithmRegistry algorithmRegistry,
        ILogger<RecorderBindingFile> logger)
    {
        _sourceProvider = sourceProvider;
        _cryptoPolicy = cryptoPolicy;
        _algorithmRegistry = algorithmRegistry;
        _logger = logger;
    }

    public async Task WriteAsync(
        string recorderRootPath,
        BindingInfo binding,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recorderRootPath);
        ArgumentNullException.ThrowIfNull(binding);

        // 1) 策略：取 MAC 算法
        var macProvider = await _cryptoPolicy
            .GetMacProviderAsync(CryptoUsage.RecorderBinding, ct)
            .ConfigureAwait(false);

        // 2) 密钥：从主密钥 HKDF 派生
        var key = await _cryptoPolicy
            .GetBindingMacKeyAsync(ct)
            .ConfigureAwait(false);

        // 3) 协议：调 BindingFileCodec（唯一实现）
        var mac = BindingFileCodec.ComputeMac(binding, key, macProvider);
        var content = BindingFileCodec.Serialize(binding, macProvider.Algorithm, mac);

        // 4) IO
        var store = GetFileStore(recorderRootPath);
        store.WriteFile(recorderRootPath, BindingFileCodec.FileName, content);

        _logger.LogDebug("绑定文件已写入：{Path}（MAC 算法 {Algo}）",
            recorderRootPath, macProvider.Algorithm);
    }

    // 读取
    public BindingInfo? Read(string recorderRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recorderRootPath);

        var store = GetFileStore(recorderRootPath);
        var text = store.ReadFile(recorderRootPath, BindingFileCodec.FileName);
        if (text is null) return null;

        var info = BindingFileCodec.Parse(text);
        if (info is null)
            _logger.LogWarning("绑定文件格式错误：{Path}", recorderRootPath);

        return info;
    }

    // 验证
    public async Task<bool> VerifySignatureAsync(
        string recorderRootPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recorderRootPath);

        // 1) IO：读原始 INI 内容
        var store = GetFileStore(recorderRootPath);
        var content = store.ReadFile(recorderRootPath, BindingFileCodec.FileName);
        if (content is null) return false;

        // 2) 解析
        var info = BindingFileCodec.Parse(content);
        if (info is null || string.IsNullOrEmpty(info.Signature))
            return false;

        // 3) 密钥：从主密钥 HKDF 派生
        var key = await _cryptoPolicy
            .GetBindingMacKeyAsync(ct)
            .ConfigureAwait(false);

        // 4) 从 INI 里读 mac_algo → 解析 MAC 提供者
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

        // 5) 协议：调 BindingFileCodec（唯一实现，固定时间比较）
        var ok = BindingFileCodec.VerifyMac(info, key, macProvider);

        if (!ok)
            _logger.LogWarning("绑定文件 MAC 校验失败：{Path}", recorderRootPath);

        return ok;
    }

    // 删除
    public void Delete(string recorderRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recorderRootPath);

        var store = GetFileStore(recorderRootPath);
        store.DeleteFile(recorderRootPath, BindingFileCodec.FileName);
    }

    // 内部：IO + 协议路由
    private static ProtocolType ResolveProtocol(string recorderRoot)
    {
        if (MtpRoot.IsMtpRoot(recorderRoot)) return ProtocolType.Mtp;
        return ProtocolType.Ums;
    }

    private IRecorderFileStore GetFileStore(string recorderRoot)
    {
        var protocol = ResolveProtocol(recorderRoot);
        var source = _sourceProvider.GetFor(protocol);

        if (source is not IRecorderFileStore store)
            throw new InvalidOperationException(
                $"采集源 {source.GetType().Name} 未实现 IRecorderFileStore，无法读写绑定文件");

        return store;
    }
}
