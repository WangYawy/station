using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Station.Application.Collecting;
using Station.Application.IdGenerators;
using Station.Application.Services;
using Station.Contracts;
using Station.Contracts.Commands;
using Station.Domain.Entities;
using Station.Domain.Repositories;

namespace Station.Application.PlatformSync;

/// <summary>
/// P0 六类指令执行器：清缓存/自检/停止-启动采集 真实执行；
/// 重启服务由宿主进程接入（P2），重拉配置由同步 Worker 下一轮应用。
/// </summary>
public sealed class CommandExecutor : ICommandExecutor
{
    private readonly ICollectControl _collectControl;
    private readonly CollectOptions _collectOptions;
    private readonly IIdGenerator _idGenerator;
    private readonly ILogger<CommandExecutor> _logger;
    private readonly IDatabaseHealthService _dbHealthService;
    private readonly ILoopRepository<User> _users;
    private readonly ILoopRepository<Recorder> _recorders;

    public CommandExecutor(
        ICollectControl collectControl,
        CollectOptions collectOptions,
        IIdGenerator idGenerator,
        IDatabaseHealthService dbHealthService,
        ILoopRepository<User> users,
        ILoopRepository<Recorder> recorders,
        ILogger<CommandExecutor> logger)
    {
        _collectControl = collectControl;
        _collectOptions = collectOptions;
        _idGenerator = idGenerator;
        _logger = logger;
        _dbHealthService = dbHealthService;
        _users = users;
        _recorders = recorders;
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
            CommandType.WriteBinding => WriteBinding(command.PayloadJson),
            _ => "未知指令"
        };

        _logger.LogInformation("执行远程指令 {CommandId}（{Type}）：{Message}", command.CommandId, command.Type, message);
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
            var isConnected = _dbHealthService.IsConnected();
            checks.Add(isConnected ? "数据库连接正常" : $"数据库响应异常");
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

    /// <summary>写入记录仪绑定：更新本机台账，记录仪下次接入时由识别流程自动重写 ini。</summary>
    private string WriteBinding(string? payloadJson)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<WriteBindingPayload>(
                payloadJson ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null || string.IsNullOrWhiteSpace(payload.RecorderSerial))
            {
                return "绑定指令缺少记录仪编号";
            }

            var user = payload.UserNo is null
                ? null
                : _users.First(u => u.UserNo == payload.UserNo && u.IsActive);
            if (user is null)
            {
                return $"用户 {payload.UserNo} 未同步到本机，绑定未生效";
            }

            var recorder = _recorders.First(r => r.SerialNumber == payload.RecorderSerial);
            if (recorder is null)
            {
                recorder = new Recorder
                {
                    Id = _idGenerator.NextId(),
                    SerialNumber = payload.RecorderSerial,
                    Model = payload.Model ?? string.Empty,
                    Protocol = payload.Protocol is { } protocol ? (ProtocolType)protocol : ProtocolType.Ums,
                    BoundUserId = user.Id,
                    DeptId = payload.DeptId ?? user.DeptId,
                    IsAuthorized = true,
                    IsActive = true
                };
                _recorders.Insert(recorder);
            }
            else
            {
                recorder.Model = payload.Model ?? recorder.Model;
                recorder.BoundUserId = user.Id;
                recorder.DeptId = payload.DeptId ?? user.DeptId;
                recorder.IsAuthorized = true;
                _recorders.Update(recorder);
            }

            return "绑定已更新，记录仪下次接入时自动写入绑定文件";
        }
        catch (Exception ex)
        {
            return $"绑定指令执行失败：{ex.Message}";
        }
    }
}
