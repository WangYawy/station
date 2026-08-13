using Station.Contracts.Sync;

namespace Station.Application.PlatformSync;

/// <summary>配置应用服务：按 EntityType 热更新采集/存储策略并记录已应用版本。</summary>
public interface IConfigApplyService
{
    Task<int> ApplyAsync(ConfigSyncResponse response);
}
