using System.Diagnostics;
using System.Net.Sockets;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.Collecting;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Contracts.Commands;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using ProtocolType = Station.Contracts.ProtocolType;

// ---------------------------------------------------------------------------
// M14 Spike: 平台指令下发→采集站执行→回执 + 停止/恢复采集 + 配置同步拉取
// ---------------------------------------------------------------------------

var testRoot = @"E:\Reny\station\archive\command-test";
var simDir = Path.Combine(testRoot, "sim");
var dbFile = Path.Combine(testRoot, "station.db");
if (Directory.Exists(testRoot))
{
    Directory.Delete(testRoot, true);
}

Directory.CreateDirectory(testRoot);

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(testRoot, "cache"));
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", simDir);
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDFILECOUNT", "2");
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDCHUNKDELAYMS", "5");
Environment.SetEnvironmentVariable("STATION__PLATFORM__ENABLED", "true");
Environment.SetEnvironmentVariable("STATION__PLATFORM__BASEURL", "http://127.0.0.1:5103");
Environment.SetEnvironmentVariable("STATION__PLATFORM__STATIONCODE", "ST-CMD");
Environment.SetEnvironmentVariable("STATION__PLATFORM__SYNCINTERVALSECONDS", "3");
Environment.SetEnvironmentVariable("STATION__PLATFORM__COMMANDPOLLINTERVALSECONDS", "3");
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");

const int PlatformPort = 5103;
var platformExe = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api\bin\Release\net8.0\Station.Platform.Api.exe";
var platform = Process.Start(new ProcessStartInfo
{
    FileName = platformExe,
    Arguments = $"--urls http://127.0.0.1:{PlatformPort}",
    WorkingDirectory = Path.GetDirectoryName(platformExe)!,
    UseShellExecute = false,
    CreateNoWindow = true,
    Environment = { ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{PlatformPort}" }
});

var deadline = DateTime.Now.AddSeconds(30);
while (DateTime.Now < deadline)
{
    try
    {
        using var probe = new TcpClient();
        probe.Connect("127.0.0.1", PlatformPort);
        break;
    }
    catch
    {
        await Task.Delay(500);
    }
}

var host = HostBuilderFactory.Create().Build();
try
{
    await host.StartAsync();

    var context = host.Services.GetRequiredService<IStationContext>();
    var registerDeadline = DateTime.Now.AddSeconds(30);
    while (context.StationId is null && DateTime.Now < registerDeadline)
    {
        await Task.Delay(300);
    }

    var stationId = context.StationId!.Value;
    Pass("平台注册", stationId > 0, $"StationId={stationId}");

    using var http = new HttpClient();

    // ---------- 1. 下发三类指令 ----------
    long clearCacheId = 0, selfCheckId = 0, stopCollectingId = 0;
    try
    {
        async Task<long> Dispatch(CommandType type)
        {
            var resp = await http.PostAsJsonAsync(
                $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/commands",
                new { type });
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<DispatchResponse>();
            return body!.Data!.CommandId;
        }

        clearCacheId = await Dispatch(CommandType.ClearCache);
        selfCheckId = await Dispatch(CommandType.RunSelfCheck);
        stopCollectingId = await Dispatch(CommandType.StopCollecting);
        Pass("平台下发指令", clearCacheId > 0 && selfCheckId > 0 && stopCollectingId > 0,
            $"清缓存={clearCacheId}, 自检={selfCheckId}, 停止采集={stopCollectingId}");
    }
    catch (Exception ex)
    {
        Pass("平台下发指令", false, ex.Message);
    }

    // ---------- 2. 采集站执行 + 回执 ----------
    try
    {
        var commands = host.Services.GetRequiredService<ICommandService>();
        var outbox = host.Services.GetRequiredService<ISyncOutboxService>();
        var pulled = await commands.PollAndExecuteAsync(stationId);
        var sent = await outbox.DrainAsync();

        var list = await http.GetFromJsonAsync<CommandListResponse>(
            $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/commands");
        var executed = list!.Data!.Where(c => c.Status == CommandStatus.Succeeded).ToList();
        var messages = string.Join(" | ", executed.Select(c => c.Message));
        var ok = pulled == 3 && sent == 3 && executed.Count == 3
                 && messages.Contains("缓存") && messages.Contains("自检") && messages.Contains("停止采集");
        Pass("执行+回执", ok, $"执行={pulled}, 回执上报={sent}, 成功回执={executed.Count}, 消息={messages}");
    }
    catch (Exception ex)
    {
        Pass("执行+回执", false, ex.Message);
    }

    // ---------- 3. 停止采集后拒绝新任务 ----------
    try
    {
        var collect = host.Services.GetRequiredService<ICollectTaskService>();
        var rejected = false;
        try
        {
            await collect.CreateTaskAsync(
                new CollectDeviceInfo("记录仪-CMD", "SIM-CMD", ProtocolType.Ums),
                isAuto: true);
        }
        catch (InvalidOperationException ex)
        {
            rejected = ex.Message.Contains("采集已停止");
        }

        Pass("停止采集生效", rejected, $"新任务被拒={rejected}");
    }
    catch (Exception ex)
    {
        Pass("停止采集生效", false, ex.Message);
    }

    // ---------- 4. 恢复采集后任务可创建 ----------
    try
    {
        var dispatchResp = await http.PostAsJsonAsync(
            $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/commands",
            new { type = (int)CommandType.StartCollecting });
        dispatchResp.EnsureSuccessStatusCode();

        var commands = host.Services.GetRequiredService<ICommandService>();
        var outbox = host.Services.GetRequiredService<ISyncOutboxService>();
        await commands.PollAndExecuteAsync(stationId);
        await outbox.DrainAsync();

        var collect = host.Services.GetRequiredService<ICollectTaskService>();
        var task = await collect.CreateTaskAsync(
            new CollectDeviceInfo("记录仪-CMD", "SIM-CMD", ProtocolType.Ums),
            isAuto: true);
        var taskDeadline = DateTime.Now.AddSeconds(60);
        while (DateTime.Now < taskDeadline)
        {
            var current = await collect.GetTaskAsync(task.TaskId);
            if (current is not null && current.Status is
                CollectTaskStatus.Completed or CollectTaskStatus.Interrupted
                or CollectTaskStatus.Failed or CollectTaskStatus.Canceled)
            {
                break;
            }

            await Task.Delay(150);
        }

        var done = await collect.GetTaskAsync(task.TaskId);
        Pass("恢复采集生效", done is { Status: CollectTaskStatus.Completed },
            $"任务状态={done?.Status}");
    }
    catch (Exception ex)
    {
        Pass("恢复采集生效", false, ex.Message);
    }

    // ---------- 5. 配置同步拉取 ----------
    try
    {
        var configState = host.Services.GetRequiredService<IConfigSyncState>();
        var configDeadline = DateTime.Now.AddSeconds(30);
        while (configState.LastSyncAt is null && DateTime.Now < configDeadline)
        {
            await Task.Delay(500);
        }

        Pass("配置同步拉取", configState.LastSyncAt is not null,
            $"版本={configState.Version}, 变更数={configState.ChangeCount}, 最近同步={configState.LastSyncAt:HH:mm:ss}");
    }
    catch (Exception ex)
    {
        Pass("配置同步拉取", false, ex.Message);
    }

    // ---------- 6. 清理 ----------
    try
    {
        var db = host.Services.GetRequiredService<ISqlSugarClient>();
        db.DbMaintenance.DropTable<SyncOutbox>();
        db.DbMaintenance.DropTable<VideoFile>();
        db.DbMaintenance.DropTable<CollectFile>();
        db.DbMaintenance.DropTable<CollectTask>();
        Pass("清理", true, "已删除采集站测试表");
    }
    catch (Exception ex)
    {
        Pass("清理", false, ex.Message);
    }
}
finally
{
    await host.StopAsync();
    host.Dispose();
    if (platform is not null && !platform.HasExited)
    {
        platform.Kill();
    }
}

Console.WriteLine();
Console.WriteLine("================ 平台指令闭环验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record DispatchResponse(bool Success, int Code, string Message, DispatchData? Data);

internal sealed record DispatchData(long CommandId, int Status, string Message);

internal sealed record CommandListResponse(bool Success, int Code, string Message, List<CommandView>? Data);

internal sealed record CommandView(long CommandId, int Type, CommandStatus Status, DateTime IssuedAt, DateTime? FinishedAt, string? Message);
