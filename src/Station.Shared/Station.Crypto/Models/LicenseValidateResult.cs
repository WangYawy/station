namespace Station.Crypto;

/// <summary>授权验证结果。</summary>
/// <param name="Valid">是否有效。</param>
/// <param name="Message">结果描述（中文，可直接展示）。</param>
/// <param name="Payload">解析出的 payload（验签失败时为 null）。</param>
public sealed record LicenseValidateResult(
    bool Valid,
    string Message,
    LicensePayload? Payload);
