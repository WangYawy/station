using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.Collecting;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Contracts.Sync;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Storage;
using ProtocolType = Station.Contracts.ProtocolType;

// ---------------------------------------------------------------------------
// M15 Spike: 平台配置发布 → 采集站热更新生效 → 增量拉取不重复
// ---------------------------------------------------------------------------

var testRoot = @"E:\Reny\station\archive\config-test";
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
Environment.SetEnvironmentVariable("STATION__PLATFORM__BASEURL", "http://127.0.0.1:5104");
Environment.SetEnvironmentVariable("STATION__PLATFORM__STATIONCODE", "ST-CFG");
Environment.SetEnvironmentVariable("STATION__PLATFORM__SYNCINTERVALSECONDS", "3");
Environment.SetEnvironmentVariable("STATION__PLATFORM__COMMANDPOLLINTERVALSECONDS", "3");
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");

const int PlatformPort = 5104;
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
    using var http = new HttpClient();

    // ---------- 1. 平台发布两条配置变更 ----------
    try
    {
        var collectResp = await http.PostAsJsonAsync(
            $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/configs",
            new { entityType = "CollectPolicy", operation = 0, payloadJson = "{\"autoCollectOnConnect\":false,\"eraseAfterComplete\":true}" });
        collectResp.EnsureSuccessStatusCode();
        var storageResp = await http.PostAsJsonAsync(
            $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/configs",
            new { entityType = "StoragePolicy", operation = 0, payloadJson = "{\"retryCount\":5,\"circuitBreakerThreshold\":8}" });
        storageResp.EnsureSuccessStatusCode();

        var list = await http.GetFromJsonAsync<ConfigListResponse>(
            $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/configs");
        Pass("平台发布配置", list?.Data?.Count == 2,
            $"已发布变更数={list?.Data?.Count}");
    }
    catch (Exception ex)
    {
        Pass("平台发布配置", false, ex.Message);
    }

    // ---------- 2. 采集站热更新生效（等待 Worker 拉取应用） ----------
    try
    {
        var state = host.Services.GetRequiredService<IConfigSyncState>();
        var applyDeadline = DateTime.Now.AddSeconds(30);
        while (state.AppliedVersions.Count < 2 && DateTime.Now < applyDeadline)
        {
            await Task.Delay(500);
        }

        var collect = host.Services.GetRequiredService<CollectOptions>();
        var storage = host.Services.GetRequiredService<StorageOptions>();
        var ok = !collect.AutoCollectOnConnect
                 && collect.EraseAfterComplete
                 && storage.RetryCount == 5
                 && storage.CircuitBreakerThreshold == 8
                 && state.AppliedVersions.ContainsKey("CollectPolicy")
                 && state.AppliedVersions.ContainsKey("StoragePolicy");
        Pass("配置热更新生效", ok,
            $"AutoCollect={collect.AutoCollectOnConnect}, Erase={collect.EraseAfterComplete}, Retry={storage.RetryCount}, 熔断阈值={storage.CircuitBreakerThreshold}, 已应用={string.Join(",", state.AppliedVersions.Select(kv => $"{kv.Key}@{kv.Value}"))}");
    }
    catch (Exception ex)
    {
        Pass("配置热更新生效", false, ex.Message);
    }

    // ---------- 3. 热更新即刻生效于新任务（自动采集已关） ----------
    try
    {
        var collect = host.Services.GetRequiredService<ICollectTaskService>();
        var task = await collect.CreateTaskAsync(
            new CollectDeviceInfo("记录仪-CFG", "SIM-CFG", ProtocolType.Ums),
            isAuto: true);
        Pass("自动采集已关", task.Status == CollectTaskStatus.Created,
            $"自动任务状态={task.Status}（应 Created，未自动开始）");
    }
    catch (Exception ex)
    {
        Pass("自动采集已关", false, ex.Message);
    }

    // ---------- 4. 增量拉取：已应用版本后不再返回变更 ----------
    try
    {
        var client = host.Services.GetRequiredService<IPlatformClient>();
        var state = host.Services.GetRequiredService<IConfigSyncState>();
        var response = await client.SyncConfigAsync(new ConfigSyncRequest
        {
            StationId = stationId,
            AppliedVersions = new Dictionary<string, long>(state.AppliedVersions)
        }, CancellationToken.None);
        Pass("增量拉取不重复", response is { Changes.Count: 0 },
            $"再次拉取变更数={response?.Changes.Count}, 最新版本={response?.Version}");
    }
    catch (Exception ex)
    {
        Pass("增量拉取不重复", false, ex.Message);
    }

    // ---------- 5. 审计留痕 ----------
    try
    {
        var audit = host.Services.GetRequiredService<Station.Application.Audit.IAuditLogService>();
        var logs = await audit.GetRecentAsync(20);
        var applyLogs = logs.Where(l => l.OperationType == "config-apply").ToList();
        Pass("审计留痕", applyLogs.Count >= 2,
            $"config-apply 审计={applyLogs.Count} 条");
    }
    catch (Exception ex)
    {
        Pass("审计留痕", false, ex.Message);
    }

    // ---------- 6. 清理 ----------
    try
    {
        var db = host.Services.GetRequiredService<ISqlSugarClient>();
        db.DbMaintenance.DropTable<SyncOutbox>();
        db.DbMaintenance.DropTable<VideoFile>();
        db.DbMaintenance.DropTable<CollectFile>();
        db.DbMaintenance.DropTable<CollectTask>();
        db.DbMaintenance.DropTable<AuditLog>();
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
Console.WriteLine("================ 配置同步热更新验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record ConfigListResponse(bool Success, int Code, string Message, List<ConfigItem>? Data);

internal sealed record ConfigItem(string EntityType, int Operation, string PayloadJson, long Version, DateTime PublishedAt);
