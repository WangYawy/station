namespace Station.Crypto.Engine.Binding;

/// <summary>
/// 记录仪绑定文件引擎。
///
/// 【职责】仅做"编排"，核心协议实现在 <see cref="BindingFileCodec"/>。
/// 【调用方】Tools.BindingFileTool（Application 层直接用 BindingFileCodec）。
/// </summary>
public static class BindingEngine
{
    /// <summary>
    /// 生成绑定文件内容。
    /// </summary>
    public static string Build(
        BindingInfo info,
        byte[] bindingKey,
        IMacProvider macProvider)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(bindingKey);
        ArgumentNullException.ThrowIfNull(macProvider);

        var mac = BindingFileCodec.ComputeMac(info, bindingKey, macProvider);
        return BindingFileCodec.Serialize(info, macProvider.Algorithm, mac);
    }

    /// <summary>
    /// 验证绑定文件内容。
    /// </summary>
    /// <param name="content">INI 文本。</param>
    /// <param name="bindingKey">已派生的 Binding MAC 密钥。</param>
    /// <param name="macResolver">按算法标识解析 MAC 的委托（从 INI 里读 mac_algo）。</param>
    public static bool Validate(
        string content,
        byte[] bindingKey,
        Func<string, IMacProvider> macResolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(bindingKey);
        ArgumentNullException.ThrowIfNull(macResolver);

        var info = BindingFileCodec.Parse(content);
        if (info is null) return false;

        var macAlgo = BindingFileCodec.GetMacAlgo(content) ?? CryptoAlgorithm.HmacSm3;

        IMacProvider mac;
        try { mac = macResolver(macAlgo); }
        catch (NotSupportedException) { return false; }

        return BindingFileCodec.VerifyMac(info, bindingKey, mac);
    }

    /// <summary>
    /// 查看绑定信息（不做验证）。
    /// </summary>
    public static BindingInfo? Inspect(string content) => BindingFileCodec.Parse(content);
}
