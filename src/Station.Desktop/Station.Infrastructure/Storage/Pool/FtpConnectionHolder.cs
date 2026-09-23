using System.Net;
using FluentFTP;
using Microsoft.Extensions.Logging;
using Station.Application.Storage;

namespace Station.Infrastructure.Storage.Pool;

/// <summary>
/// FluentFTP 连接持有者（适配 FluentFTP 54.2.0）。
/// 
/// 【保活机制】
///   设置 client.Config.NoopInterval，由 FluentFTP 内部自动发送 NOOP。
/// </summary>
public sealed class FtpConnectionHolder : IAsyncDisposable
{
    private readonly StorageTargetConfig _config;
    private readonly int _keepAliveSeconds;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private AsyncFtpClient? _client;
    private bool _disposed;

    public FtpConnectionHolder(StorageTargetConfig config, int keepAliveSeconds, ILogger logger)
    {
        _config = config;
        _keepAliveSeconds = Math.Max(0, keepAliveSeconds);
        _logger = logger;
    }

    /// <summary>获取已连接的客户端（惰性连接 + 断线自动重连）。</summary>
    public async Task<AsyncFtpClient> GetConnectedAsync(string plainPassword, CancellationToken ct)
    {
        if (_client is { IsConnected: true }) return _client;

        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is { IsConnected: true }) return _client;

            // 断开则重建
            if (_client is not null)
            {
                try { await _client.DisposeAsync().ConfigureAwait(false); } catch { }
                _client = null;
            }

            var client = new AsyncFtpClient(_config.FtpHost!, _config.FtpPort)
            {
                Credentials = new NetworkCredential(_config.FtpUser ?? string.Empty, plainPassword),
                Encoding = System.Text.Encoding.UTF8 // 中文编码：显式指定 UTF-8（现代 FTP 服务器推荐） 
            };

            // ---- FluentFTP 54.x：超时配置通过 client.Config 设置 ----
            client.Config.ConnectTimeout = 10_000;                  // 连接超时 10s
            client.Config.ReadTimeout = 30_000;                     // 读取超时 30s
            client.Config.DataConnectionConnectTimeout = 10_000;    // 数据连接建立超时 10s
            client.Config.DataConnectionReadTimeout = 30_000;       // 数据连接读取超时 30s
                                                                    // 中文编码：显式指定 UTF-8（现代 FTP 服务器推荐）

            // ---- FluentFTP 54.x：自动保活（替代手动 Noop） ----
            if (_keepAliveSeconds > 0)
            {
                client.Config.NoopInterval = _keepAliveSeconds * 1000;  // 毫秒
            }

            await client.Connect(ct).ConfigureAwait(false);
            _client = client;

            _logger.LogInformation("[ftp:{Target}] 已连接 {Host}:{Port}",
                _config.Name ?? "default", _config.FtpHost, _config.FtpPort);

            return client;
        }
        finally { _connectLock.Release(); }
    }

    /// <summary>强制标记连接失效（下次调用将重连）。</summary>
    public void MarkBroken()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_client is not null)
        {
            try { await _client.Disconnect(CancellationToken.None).ConfigureAwait(false); } catch { }
            await _client.DisposeAsync().ConfigureAwait(false);
        }
        _connectLock.Dispose();
    }
}
