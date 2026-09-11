namespace Station.Application.Recorders;

/// <summary>记录仪绑定配置，对应配置节 <c>Station:Binding</c>。</summary>
public sealed class BindingOptions
{
    public const string SectionName = "Station:Binding";

    /// <summary>
    /// 是否启用根目录配置文件验证
    /// </summary>
    public bool EnableProfile { get; set; } = true;
    /// <summary>
    /// 是否启用绑定
    /// </summary>
    public bool EnableBinding { get; set; } = true;
    /// <summary>
    /// 是否启用加密
    /// </summary>
    public bool EnableSecret { get; set; }

    /// <summary>ini 签名密钥（SM3 盐，防篡改）。</summary>
    public string Secret { get; set; } = "station-dev-binding-secret";

}
