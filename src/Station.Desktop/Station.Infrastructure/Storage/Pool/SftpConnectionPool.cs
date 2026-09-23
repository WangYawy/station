using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Renci.SshNet;
using Station.Application.Storage;

namespace Station.Infrastructure.Storage.Pool;

/// <summary>
/// SFTP 连接池（适配 SSH.NET 2024.2.0）。
/// 
/// 【API 变更说明】
///   KeepAliveInterval 不在 ConnectionInfo 上，而在 SftpClient 实例上：
///     client.KeepAliveInterval = TimeSpan.FromSeconds(30);
///   设置后 SSH.NET 内部自动发送 SSH_MSG_IGNORE 保活包。
/// 
/// 【连接池策略】
///   - 借出时检查 IsConnected，断开则丢弃重建；
///   - 归还时若连接有效则放回池中，否则丢弃。
/// </summary>
public sealed class SftpConnectionPool : IDisposable
{
    private readonly StorageTargetConfig _config;
    private readonly int _keepAliveSeconds;
    private readonly ILogger _logger;
    private readonly ObjectPool<SftpClient> _pool;
    private readonly Func<string> _passwordProvider;
    private bool _disposed;

    public SftpConnectionPool(
        StorageTargetConfig config,
        int poolSize,
        int keepAliveSeconds,
        Func<string> passwordProvider,
        ILogger logger)
    {
        _config = config;
        _keepAliveSeconds = Math.Max(0, keepAliveSeconds);
        _passwordProvider = passwordProvider;
        _logger = logger;

        var provider = new DefaultObjectPoolProvider { MaximumRetained = Math.Max(1, poolSize) };
        _pool = provider.Create(new Policy(this));
    }

    public SftpClient Rent()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SftpConnectionPool));

        var client = _pool.Get();

        // 借出时若连接已断，重建
        if (!client.IsConnected)
        {
            try { client.Connect(); }
            catch
            {
                SafeDispose(client);
                client = CreateClient();
                client.Connect();
            }
        }
        return client;
    }

    public void Return(SftpClient client)
    {
        if (client is null) return;
        if (_disposed || !client.IsConnected)
        {
            SafeDispose(client);
            return;
        }
        _pool.Return(client);
    }

    internal SftpClient CreateClient()
    {
        var user = _config.SftpUser ?? string.Empty;
        var password = _passwordProvider();

        var passwordAuth = new PasswordAuthenticationMethod(user, password);
        var keyboardAuth = new KeyboardInteractiveAuthenticationMethod(user);
        keyboardAuth.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts) prompt.Response = password;
        };

        var connInfo = new ConnectionInfo(
            _config.SftpHost!, _config.SftpPort, user, passwordAuth, keyboardAuth)
        {
            Timeout = TimeSpan.FromSeconds(15)
            // ConnectionInfo 没有 KeepAliveInterval 属性
        };

        var client = new SftpClient(connInfo);

        // SSH.NET 2024.2.0：KeepAliveInterval 在 SftpClient 实例上
        if (_keepAliveSeconds > 0)
        {
            client.KeepAliveInterval = TimeSpan.FromSeconds(_keepAliveSeconds);
        }

        return client;
    }

    private static void SafeDispose(SftpClient c)
    {
        try { c.Disconnect(); } catch { }
        try { c.Dispose(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    private sealed class Policy : PooledObjectPolicy<SftpClient>
    {
        private readonly SftpConnectionPool _owner;
        public Policy(SftpConnectionPool owner) => _owner = owner;

        public override SftpClient Create() => _owner.CreateClient();

        public override bool Return(SftpClient obj)
        {
            if (obj is null || !obj.IsConnected) return false;
            return true;
        }
    }
}
