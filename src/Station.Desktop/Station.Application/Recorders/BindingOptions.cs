namespace Station.Application.Recorders;

/// <summary>记录仪绑定配置，对应配置节 <c>Station:Binding</c>。</summary>
public sealed class BindingOptions
{
    public const string SectionName = "Station:Binding";

    /// <summary>
    /// 【总开关】是否启用绑定文件验证。
    /// false = 不读 station_bind.ini，直接识别成功（适用于"无绑定模式"）。
    /// </summary>
    public bool EnableBinding { get; set; } = true;
    /// <summary>
    /// 是否验证绑定文件 MAC（HMAC-SM3）。
    /// 仅在 EnableBinding=true 时生效。
    /// false = 跳过 MAC 校验，直接进入台账比对。
    /// </summary>
    public bool EnableSignature { get; set; } = true;
    /// <summary>
    /// 是否验证用户和部门。
    /// 仅在 EnableBinding=true 时生效。
    /// false = 只校验设备（序列号 + 台账在册），不校验用户/部门。
    /// true  = 校验设备 + 用户 + 部门。
    /// </summary>
    public bool EnableBindingValidation { get; set; } = true;

}
