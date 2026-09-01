namespace Station.Application.Recorders;

/// <summary>记录仪绑定配置，对应配置节 <c>Station:Binding</c>。</summary>
public sealed class BindingOptions
{
    public const string SectionName = "Station:Binding";

    /// <summary>ini 签名密钥（SM3 盐，防篡改）。</summary>
    public string Secret { get; set; } = "station-dev-binding-secret";
}
