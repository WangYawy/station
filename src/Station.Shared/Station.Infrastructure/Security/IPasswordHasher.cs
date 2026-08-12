namespace Station.Infrastructure.Security;

/// <summary>密码哈希接口（PBKDF2-HMAC-SM3，国密合规）。</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string storedHash);
}
