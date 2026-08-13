using System.Text.Json;
using Station.Application.Alerts;
using Station.Application.Audit;
using Station.Application.Collecting;
using Station.Contracts;
using Station.Contracts.Sync;
using Station.Domain.Entities;
using Station.Infrastructure.Storage;

namespace Station.Application.PlatformSync;

public sealed class ConfigApplyService : IConfigApplyService
{
    private readonly CollectOptions _collectOptions;
    private readonly StorageOptions _storageOptions;
    private readonly IConfigSyncState _state;
    private readonly IAuditLogService _audit;

    public ConfigApplyService(
        CollectOptions collectOptions,
        StorageOptions storageOptions,
        IConfigSyncState state,
        IAuditLogService audit)
    {
        _collectOptions = collectOptions;
        _storageOptions = storageOptions;
        _state = state;
        _audit = audit;
    }

    public async Task<int> ApplyAsync(ConfigSyncResponse response)
    {
        var applied = 0;
        foreach (var change in response.Changes)
        {
            var detail = change.EntityType switch
            {
                "CollectPolicy" => ApplyCollectPolicy(change),
                "StoragePolicy" => ApplyStoragePolicy(change),
                _ => $"暂不支持配置类型 {change.EntityType}，已跳过"
            };

            _state.RecordApplied(change.EntityType, change.Version);
            applied++;
            await _audit.WriteAsync(new AuditLog
            {
                OperatorAccount = "platform",
                OperationType = "config-apply",
                Target = change.EntityType,
                Detail = $"v{change.Version}：{detail}",
                Result = detail.StartsWith("暂不支持") ? 0 : 1,
                CreatedAt = DateTime.Now
            });
        }

        return applied;
    }

    private string ApplyCollectPolicy(ConfigChangeItem change)
    {
        using var json = JsonDocument.Parse(change.PayloadJson);
        var root = json.RootElement;
        if (root.TryGetProperty("autoCollectOnConnect", out var auto) && auto.ValueKind == JsonValueKind.True)
        {
            _collectOptions.AutoCollectOnConnect = true;
        }
        else if (root.TryGetProperty("autoCollectOnConnect", out var autoFalse) && autoFalse.ValueKind == JsonValueKind.False)
        {
            _collectOptions.AutoCollectOnConnect = false;
        }

        if (root.TryGetProperty("eraseAfterComplete", out var erase))
        {
            _collectOptions.EraseAfterComplete = erase.ValueKind == JsonValueKind.True;
        }

        if (root.TryGetProperty("skipCollected", out var skip))
        {
            _collectOptions.SkipCollected = skip.ValueKind == JsonValueKind.True;
        }

        return $"采集策略已热更新（AutoCollect={_collectOptions.AutoCollectOnConnect}, Erase={_collectOptions.EraseAfterComplete}, Skip={_collectOptions.SkipCollected}）";
    }

    private string ApplyStoragePolicy(ConfigChangeItem change)
    {
        using var json = JsonDocument.Parse(change.PayloadJson);
        var root = json.RootElement;
        if (root.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String)
        {
            _storageOptions.Target = Enum.TryParse<StorageTargetKind>(target.GetString(), true, out var kind)
                ? kind
                : _storageOptions.Target;
        }

        if (root.TryGetProperty("directoryTemplate", out var template) && template.ValueKind == JsonValueKind.String)
        {
            _storageOptions.DirectoryTemplate = template.GetString()!;
        }

        if (root.TryGetProperty("retryCount", out var retry) && retry.TryGetInt32(out var retryValue))
        {
            _storageOptions.RetryCount = retryValue;
        }

        if (root.TryGetProperty("circuitBreakerThreshold", out var breaker) && breaker.TryGetInt32(out var breakerValue))
        {
            _storageOptions.CircuitBreakerThreshold = breakerValue;
        }

        return $"存储策略已热更新（Target={_storageOptions.Target}, Retry={_storageOptions.RetryCount}, 熔断阈值={_storageOptions.CircuitBreakerThreshold}）";
    }
}
