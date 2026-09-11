namespace Station.Domain.Security;
/// <summary>
/// 配置敏感信息
/// </summary>
public interface ISecretProtector
{
    string Protect(string value);
    string? TryUnprotect(string? value);
}
