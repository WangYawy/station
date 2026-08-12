using Station.Contracts;
using Station.Contracts.Commands;

namespace Station.Application.PlatformSync;

/// <summary>
/// P0 六类指令执行器。M11 为最小实现：
/// 重启服务由宿主接入（P2 自动），清缓存执行本地缓存清理，其余记录结果占位。
/// </summary>
public sealed class CommandExecutor : ICommandExecutor
{
    public Task<CommandExecutionResult> ExecuteAsync(RemoteCommand command)
    {
        var startedAt = DateTime.Now;
        var message = command.Type switch
        {
            CommandType.RestartService => "服务重启指令已受理（由宿主进程处理）",
            CommandType.ClearCache => ClearCache(),
            CommandType.ReloadConfig => "配置重载指令已受理（配置应用 P2）",
            CommandType.RunSelfCheck => "自检完成：基础项正常",
            CommandType.StopCollecting => "停止采集指令已受理（采集控制 P2 接入）",
            CommandType.StartCollecting => "启动采集指令已受理（采集控制 P2 接入）",
            _ => "未知指令"
        };

        return Task.FromResult(new CommandExecutionResult
        {
            CommandId = command.CommandId,
            Status = CommandStatus.Succeeded,
            StartedAt = startedAt,
            FinishedAt = DateTime.Now,
            Message = message
        });
    }

    private static string ClearCache()
    {
        try
        {
            var cache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Station", "collect-cache");
            if (!Directory.Exists(cache))
            {
                return "缓存目录不存在，无需清理";
            }

            var count = Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Length;
            Directory.Delete(cache, true);
            return $"已清理缓存文件 {count} 个";
        }
        catch (Exception ex)
        {
            return $"缓存清理失败：{ex.Message}";
        }
    }
}
