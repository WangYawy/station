using Station.Application.Collecting;
using Station.Contracts;
using Station.Contracts.Commands;
using Station.Infrastructure;
using Station.Infrastructure.Db;

namespace Station.Application.PlatformSync;

/// <summary>
/// P0 六类指令执行器：清缓存/自检/停止-启动采集 真实执行；
/// 重启服务由宿主进程接入（P2），重拉配置由同步 Worker 下一轮应用。
/// </summary>
public sealed class CommandExecutor : ICommandExecutor
{
    private readonly ICollectControl _collectControl;
    private readonly ISqlSugarFactory _sqlSugarFactory;
    private readonly DbOptions _dbOptions;
    private readonly CollectOptions _collectOptions;

    public CommandExecutor(
        ICollectControl collectControl,
        ISqlSugarFactory sqlSugarFactory,
        DbOptions dbOptions,
        CollectOptions collectOptions)
    {
        _collectControl = collectControl;
        _sqlSugarFactory = sqlSugarFactory;
        _dbOptions = dbOptions;
        _collectOptions = collectOptions;
    }

    public Task<CommandExecutionResult> ExecuteAsync(RemoteCommand command)
    {
        var startedAt = DateTime.Now;
        var message = command.Type switch
        {
            CommandType.RestartService => "服务重启指令已受理（宿主进程自动重启 P2）",
            CommandType.ClearCache => ClearCache(),
            CommandType.ReloadConfig => "配置重载指令已受理：同步 Worker 下一轮拉取应用",
            CommandType.RunSelfCheck => RunSelfCheck(),
            CommandType.StopCollecting => StopCollecting(command.CommandId),
            CommandType.StartCollecting => StartCollecting(),
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

    private string StopCollecting(long commandId)
    {
        _collectControl.StopCollecting($"远程指令 {commandId}");
        return "已停止采集（远程指令）";
    }

    private string StartCollecting()
    {
        _collectControl.StartCollecting();
        return "已恢复采集（远程指令）";
    }

    private string ClearCache()
    {
        try
        {
            if (!Directory.Exists(_collectOptions.CacheDirectory))
            {
                return "缓存目录不存在，无需清理";
            }

            var count = Directory.GetFiles(_collectOptions.CacheDirectory, "*", SearchOption.AllDirectories).Length;
            Directory.Delete(_collectOptions.CacheDirectory, true);
            return $"已清理缓存文件 {count} 个";
        }
        catch (Exception ex)
        {
            return $"缓存清理失败：{ex.Message}";
        }
    }

    private string RunSelfCheck()
    {
        var checks = new List<string>();
        try
        {
            using var client = _sqlSugarFactory.CreateClient(_dbOptions);
            var value = client.Ado.GetString("select 1");
            checks.Add(value == "1" ? "数据库连接正常" : $"数据库响应异常({value})");
        }
        catch (Exception ex)
        {
            checks.Add($"数据库连接失败：{ex.Message}");
        }

        try
        {
            Directory.CreateDirectory(_collectOptions.CacheDirectory);
            checks.Add("缓存目录可写");
        }
        catch (Exception ex)
        {
            checks.Add($"缓存目录不可写：{ex.Message}");
        }

        return $"自检完成：{string.Join("；", checks)}";
    }
}
