using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Station.Application.Collecting;
using Station.Data.Repositories;
using Station.Data.SqlSugar.Extensions;
using Station.Domain.Entities;

namespace Station.Infrastructure.Collecting;

public sealed class CollectTaskNoGenerator : ICollectTaskNoGenerator
{
    private const string Prefix = "CT";
    private const int MaxRetry = 3;

    private readonly IRepository<CollectTask> _tasks;
    private readonly ILogger<CollectTaskNoGenerator> _logger;

    /// <summary>
    /// 内存序号缓存：key = (StationCode, DeviceId, Second)，value = 已分配的最大序号。
    /// 用于减少同秒内并发创建任务时的数据库查询。
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _seqCache = new();

    public CollectTaskNoGenerator(IRepository<CollectTask> tasks, ILogger<CollectTaskNoGenerator> logger)
    {
        _tasks = tasks;
        _logger = logger;
    }

    public async Task<string> NextAsync(string stationCode, string deviceId, DateTime timestamp,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stationCode))
            throw new ArgumentException("采集站编号 不能为空。", nameof(stationCode));
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("设备编号 不能为空。", nameof(deviceId));

        var second = timestamp.ToString("yyyyMMddHHmmss");
        var cacheKey = $"{stationCode}|{deviceId}|{second}";

        for (var attempt = 1; attempt <= MaxRetry; attempt++)
        {
            var seq = AllocateSeq(cacheKey);
            var taskNo = $"{Prefix}-{stationCode}-{deviceId}-{second}-{seq:D3}";

            try
            {
                // 用轻量存在性检查预判冲突（避免直接 INSERT 依赖唯一索引抛异常）
                var exists = await _tasks.AsQueryable()
                    .Where(x => x.TaskNo == taskNo)
                    .AnyAsync(ct)
                    .ConfigureAwait(false);

                if (exists)
                {
                    _logger.LogDebug("任务编号 {TaskNo} 已存在，重试第 {Attempt} 次。", taskNo, attempt);
                    continue;
                }

                return taskNo;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "生成任务编号 {TaskNo} 时发生异常，重试第 {Attempt} 次。",
                    taskNo, attempt);
            }
        }

        // 极端情况下仍冲突，退化到毫秒级时间戳，保证不阻塞业务
        var fallbackSecond = timestamp.ToString("yyyyMMddHHmmssfff");
        var fallbackNo = $"{Prefix}-{stationCode}-{deviceId}-{fallbackSecond}-001";
        _logger.LogWarning("任务编号重试 {MaxRetry} 次后仍冲突，退化到毫秒编号：{TaskNo}",
            MaxRetry, fallbackNo);
        return fallbackNo;
    }

    private int AllocateSeq(string cacheKey)
    {
        // 先从缓存取；缓存不存在时从数据库加载当天该秒的最大序号
        // 注意：缓存只在当前秒内有效，跨秒后自动被新 key 替换，不会无限增长。
        return _seqCache.AddOrUpdate(
            cacheKey,
            _ => LoadMaxSeqFromDb(cacheKey) + 1,
            (_, old) => old + 1);
    }

    /// <summary>
    /// 从缓存 key 解析 StationCode / DeviceId / Second，查询该秒内已存在的最大序号。
    /// 缓存未命中时执行一次，后续同秒内直接走内存。
    /// </summary>
    private int LoadMaxSeqFromDb(string cacheKey)
    {
        var parts = cacheKey.Split('|');
        if (parts.Length != 3) return 0;

        var (stationCode, deviceId, second) = (parts[0], parts[1], parts[2]);
        var prefix = $"{Prefix}-{stationCode}-{deviceId}-{second}-";

        var maxTaskNo = _tasks.AsQueryable()
            .Where(x => x.TaskNo.StartsWith(prefix))
            .OrderByDescending(x => x.TaskNo)
            .Select(x => x.TaskNo)
            .First();

        if (string.IsNullOrEmpty(maxTaskNo)) return 0;

        // 任务编号尾部就是序号，取最后一段
        var lastDash = maxTaskNo.LastIndexOf('-');
        if (lastDash < 0) return 0;

        return int.TryParse(maxTaskNo[(lastDash + 1)..], out var seq) ? seq : 0;
    }

    public void CleanupExpiredCache(DateTime now)
    {
        var todayPrefix = now.ToString("yyyyMMdd");
        foreach (var key in _seqCache.Keys)
        {
            // key 形如 StationCode|DeviceId|yyyyMMddHHmmss
            var parts = key.Split('|');
            if (parts.Length == 3 && !parts[2].StartsWith(todayPrefix))
                _seqCache.TryRemove(key, out _);
        }
    }
}
