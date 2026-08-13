using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.Collecting;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Repositories;
using ProtocolType = Station.Contracts.ProtocolType;

// ---------------------------------------------------------------------------
// M13 Spike: 平台文件统一列表/检索/预览 全链路
// 迷你桌面端宿主(内置Web:5000) + 真实平台API(MySQL:5101) + 预览代理 + Range
// ---------------------------------------------------------------------------

var testRoot = @"E:\Reny\station\archive\preview-test";
var simDir = Path.Combine(testRoot, "sim");
var dbFile = Path.Combine(testRoot, "station.db");
if (Directory.Exists(testRoot))
{
    Directory.Delete(testRoot, true);
}

Directory.CreateDirectory(testRoot);

var results = new List<(string Step, bool Ok, string Detail)>();
var failedCount_ = 0;
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

// 环境配置（Host 通过环境变量读取）
Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(testRoot, "cache"));
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", simDir);
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDFILECOUNT", "3");
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDCHUNKDELAYMS", "5");
Environment.SetEnvironmentVariable("STATION__PLATFORM__ENABLED", "true");
Environment.SetEnvironmentVariable("STATION__PLATFORM__BASEURL", "http://127.0.0.1:5101");
Environment.SetEnvironmentVariable("STATION__PLATFORM__STATIONCODE", "ST-PV");
Environment.SetEnvironmentVariable("STATION__PLATFORM__STATIONBASEURL", "http://127.0.0.1:5000");
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");

const int PlatformPort = 5101;
var platformExe = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api\bin\Release\net8.0\Station.Platform.Api.exe";
var platform = Process.Start(new ProcessStartInfo
{
    FileName = platformExe,
    Arguments = $"--urls http://127.0.0.1:{PlatformPort}",
    WorkingDirectory = Path.GetDirectoryName(platformExe)!,
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    Environment =
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Development",
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{PlatformPort}"
    }
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

    // 等平台同步 Worker 完成注册
    var context = host.Services.GetRequiredService<IStationContext>();
    var registerDeadline = DateTime.Now.AddSeconds(30);
    while (context.StationId is null && DateTime.Now < registerDeadline)
    {
        await Task.Delay(300);
    }

    Pass("平台自动注册", context.StationId is { } sid && sid > 0, $"StationId={context.StationId}");

    // 采集一个任务并完成台账/上报
    var collect = host.Services.GetRequiredService<ICollectTaskService>();
    var ledger = host.Services.GetRequiredService<IFileLedgerService>();
    var outbox = host.Services.GetRequiredService<ISyncOutboxService>();
    var task = await collect.CreateTaskAsync(
        new CollectDeviceInfo("记录仪-PV", "SIM-PV", ProtocolType.Ums, UserId: 1, DeptId: 1),
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

    await ledger.ProcessCompletedTaskAsync(task.TaskId);
    await outbox.DrainAsync();

    var videoFiles = host.Services.GetRequiredService<IRepository<VideoFile>>();
    var files = await videoFiles.GetListAsync();
    var fileNo = files.First().FileNo;
    var size = files.First().Size;

    using var http = new HttpClient();

    // ---------- 1. 平台文件统一列表/检索 ----------
    try
    {
        var list = await http.GetFromJsonAsync<PlatformListResponse>(
            $"http://127.0.0.1:{PlatformPort}/api/v1/files?keyword=ST-PV&page=1&size=20");
        Pass("平台文件列表", list?.Data?.TotalCount == 3 && list!.Data!.Items.Count == 3,
            $"平台文件总数={list?.Data?.TotalCount}");
    }
    catch (Exception ex)
    {
        Pass("平台文件列表", false, ex.Message);
    }

    // ---------- 2. 采集站内置 Web 文件流端点 ----------
    try
    {
        var stream = await http.GetAsync($"http://127.0.0.1:5000/api/v1/files/{fileNo}/stream");
        var bytes = await stream.Content.ReadAsByteArrayAsync();
        Pass("采集站文件流", stream.StatusCode == HttpStatusCode.OK && bytes.Length == size,
            $"HTTP={stream.StatusCode}, 字节={bytes.Length}, 期望={size}");
    }
    catch (Exception ex)
    {
        Pass("采集站文件流", false, ex.Message);
    }

    // ---------- 3. 平台预览代理 ----------
    try
    {
        var proxy = await http.GetAsync($"http://127.0.0.1:{PlatformPort}/api/v1/files/{fileNo}/preview");
        var bytes = await proxy.Content.ReadAsByteArrayAsync();
        var ok = proxy.StatusCode == HttpStatusCode.OK && bytes.Length == size;
        Pass("平台预览代理", ok, $"HTTP={proxy.StatusCode}, 字节={bytes.Length}, 期望={size}");
        if (!ok)
        {
            var body = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1200));
            Console.WriteLine("PREVIEW_BODY: " + body);
        }
    }
    catch (Exception ex)
    {
        Pass("平台预览代理", false, ex.Message);
    }

    // ---------- 4. Range 请求（206 + 分段） ----------
    try
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"http://127.0.0.1:{PlatformPort}/api/v1/files/{fileNo}/preview");
        request.Headers.Range = new RangeHeaderValue(0, 99);
        var response = await http.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Pass("Range 分段", response.StatusCode == HttpStatusCode.PartialContent && bytes.Length == 100,
            $"HTTP={response.StatusCode}, 字节={bytes.Length}");
    }
    catch (Exception ex)
    {
        Pass("Range 分段", false, ex.Message);
    }

    // ---------- 5. 清理 ----------
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
    if (platform is not null)
    {
        var stdout = await platform.StandardOutput.ReadToEndAsync();
        var stderr = await platform.StandardError.ReadToEndAsync();
        var combined = stdout + stderr;
        if (results.Count(r => !r.Ok) > 0)
        {
            Console.WriteLine("===== 平台进程输出（失败时打印） =====");
            Console.WriteLine(combined.Length > 3000 ? combined[^3000..] : combined);
        }
    }
}

Console.WriteLine();
Console.WriteLine("================ 平台文件列表/预览 全链路验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record PlatformListResponse(bool Success, int Code, string Message, PagedData? Data);

internal sealed record PagedData(int PageIndex, int PageSize, long TotalCount, IReadOnlyList<FileItem> Items);

internal sealed record FileItem(
    long StationId, long LocalFileId, string FileNo, string FileName, long Size,
    int Kind, string Sm3, DateTime CollectedAt, string RecorderSerial,
    string? UserNo, string? DeptCode, string? StorageLocation);
