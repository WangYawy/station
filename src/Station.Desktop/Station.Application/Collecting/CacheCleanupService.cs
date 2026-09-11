namespace Station.Application.Collecting;
using Microsoft.Extensions.Logging;

/// <summary>本地加密缓存清理：按保留天数删除超期缓存文件（待上传中间态，不备份）。</summary>
public interface ICacheCleanupService
{
    Task<(int Count, long Bytes)> CleanupAsync();
}

public sealed class CacheCleanupService : ICacheCleanupService
{
    private readonly CollectOptions _options;
    private readonly ILogger<CacheCleanupService> _logger;

    public CacheCleanupService(CollectOptions options, ILogger<CacheCleanupService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<(int Count, long Bytes)> CleanupAsync()
    {
        var root = _options.CacheDirectory;
        if (!Directory.Exists(root))
        {
            return Task.FromResult((0, 0L));
        }

        var cutoff = DateTime.Now.AddDays(-_options.CacheRetentionDays);
        var count = 0;
        long bytes = 0;
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTime < cutoff)
                {
                    bytes += info.Length;
                    File.Delete(file);
                    count++;
                }
            }
            catch
            {
                // 单个文件清理失败不阻断
            }
        }

        _logger.LogInformation("缓存清理完成：{Count} 个文件，{Bytes} 字节（保留 {Days} 天）", count, bytes, _options.CacheRetentionDays);
        return Task.FromResult((count, bytes));
    }
}
