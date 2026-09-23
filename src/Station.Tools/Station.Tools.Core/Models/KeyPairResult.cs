namespace Station.Tools.Core.Models;

/// <summary>密钥对生成结果。</summary>
public sealed record KeyPairResult(string PrivatePem, string PublicPem);

/// <summary>密钥信息。</summary>
public sealed record KeyInfo(
    string Type,           // SM2 / RSA
    string Kind,           // PRIVATE / PUBLIC
    int Bits,
    string Fingerprint);   // SM3 指纹（Base64）
