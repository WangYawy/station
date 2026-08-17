using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Station.Application.Collecting;
using Station.Application.Settings;
using Station.Contracts;
using Station.Desktop.Bootstrapper;
using Station.Desktop.Infrastructure.Collecting;
using Station.Desktop.Infrastructure.Settings;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure;

// ---------------------------------------------------------------------------
// M51 Spike: 桌面端审计修复验证
//  1) 应用数据目录统一（StationPaths 重定位 + 运行时设置文件 + 宿主实际落盘）
//  2) 记录仪接入监听（稳定去重/拔出重触发/协议匹配/模拟源跳过）
// ---------------------------------------------------------------------------

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var tempRoot = Path.Combine(Path.GetTempPath(), "station-audit-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
var dataDir = Path.Combine(tempRoot, "data");

// ---------- 1. 数据目录重定位 ----------
Environment.SetEnvironmentVariable(StationPaths.DataDirEnvName, dataDir);
Pass("数据目录环境变量生效", StationPaths.DataDirectory == dataDir, StationPaths.DataDirectory);

var rebased = StationPaths.RebaseSqliteConnectionString("Data Source=station.db;Cache=Shared");
Pass("SQLite 相对路径重定位",
    rebased == $"Data Source={Path.Combine(dataDir, "station.db")};Cache=Shared",
    rebased);

var abs = StationPaths.RebaseSqliteConnectionString("Data Source=C:\\tmp\\x.db");
var memory = StationPaths.RebaseSqliteConnectionString("Data Source=:memory:");
Pass("绝对路径与内存库保持不变",
    abs == "Data Source=C:\\tmp\\x.db" && memory == "Data Source=:memory:",
    $"abs={abs}, mem={memory}");

var runtimePath = RuntimeSettingsFile.ResolvePath();
Pass("运行时设置文件落数据目录",
    runtimePath == Path.Combine(dataDir, "appsettings.runtime.json"),
    runtimePath);

// ---------- 2. 记录仪接入监听逻辑 ----------
var fake = new FakeDetector();
var handled = new List<string>();
var monitor = new RecorderConnectMonitor(
    new CollectOptions { SourceMode = "ums" },
    new IRecorderDeviceDetector[] { fake },
    d =>
    {
        handled.Add(d.Key);
        return Task.CompletedTask;
    },
    settlePolls: 3);

var simulatedMonitor = new RecorderConnectMonitor(
    new CollectOptions { SourceMode = "simulated" },
    new IRecorderDeviceDetector[] { fake },
    d => { handled.Add("sim:" + d.Key); return Task.CompletedTask; },
    settlePolls: 1);
await simulatedMonitor.CheckAsync();
Pass("模拟源不触发监听", handled.Count == 0, $"handled={handled.Count}");

// 混合协议：UMS + MTP 两个检测器同时监听（真实模式下按设备实际协议处理）
var mtpFake = new FakeMtpDetector();
var mixedMonitor = new RecorderConnectMonitor(
    new CollectOptions { SourceMode = "ums" },
    new IRecorderDeviceDetector[] { fake, mtpFake },
    d => { handled.Add("mixed:" + d.Key); return Task.CompletedTask; },
    settlePolls: 1);
mtpFake.Current.Add(new DetectedDevice("MTP:DEV\\1", "MTP相机", null, ProtocolType.Mtp, "MTP://DEV\\1"));
fake.Current.Add(new DetectedDevice("UMS:Y:\\", "第二个U盘", null, ProtocolType.Ums, "Y:\\"));
await mixedMonitor.CheckAsync();
Pass("混合协议设备同时识别",
    handled.Count == 2 && handled.Contains("mixed:UMS:Y:\\") && handled.Contains("mixed:MTP:DEV\\1"),
    string.Join(",", handled));
handled.Clear();
fake.Current.Clear();
mtpFake.Current.Clear();

var device = new DetectedDevice("UMS:X:\\", "测试U盘", null, ProtocolType.Ums, "X:\\");
fake.Current.Add(device);
await monitor.CheckAsync();
await monitor.CheckAsync();
Pass("稳定期内不提前触发", handled.Count == 0, $"handled={handled.Count}");
await monitor.CheckAsync();
Pass("连续 3 次稳定后触发一次", handled.Count == 1 && handled[0] == device.Key, string.Join(",", handled));
await monitor.CheckAsync();
await monitor.CheckAsync();
Pass("设备保持在线不重复触发", handled.Count == 1, $"handled={handled.Count}");

fake.Current.Clear();
await monitor.CheckAsync();
await monitor.CheckAsync();
fake.Current.Add(device);
await monitor.CheckAsync();
await monitor.CheckAsync();
await monitor.CheckAsync();
Pass("拔出后重新插入再次触发", handled.Count == 2, $"handled={handled.Count}");

// ---------- 3. 宿主实际启动：DB 落到数据目录 ----------
var dbDir = Path.Combine(tempRoot, "host-data");
Environment.SetEnvironmentVariable(StationPaths.DataDirEnvName, dbDir);
Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", "Data Source=station.db");
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");
Environment.SetEnvironmentVariable("STATION__WEB__PORT", "5130");
Environment.SetEnvironmentVariable("STATION__WEB__ENABLELAN", "false");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(tempRoot, "cache"));
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", Path.Combine(tempRoot, "sim"));
const int WebPort = 5130;

static async Task<HttpClient> Login(int port, string user, string pass)
{
    var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
    var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    var resp = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
    if (!resp.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"登录失败 {(int)resp.StatusCode}");
    }

    return http;
}

using (var host = HostBuilderFactory.Create().Build())
{
    await host.StartAsync();
    await Task.Delay(1500);
    var dbFile = Path.Combine(dbDir, "station.db");
    Pass("宿主启动后 DB 落在数据目录", File.Exists(dbFile), dbFile);
    Pass("运行时设置文件路径为数据目录",
        RuntimeSettingsFile.ResolvePath() == Path.Combine(dbDir, "appsettings.runtime.json"),
        RuntimeSettingsFile.ResolvePath());

    // ---------- 4. 工作台显示设置分组（Web API + 热应用） ----------
    using (var admin = await Login(WebPort, "admin", "Admin@123"))
    {
        var settings = (await admin.GetFromJsonAsync<Resp<SettingsData>>("/api/v1/settings"))?.Data;
        Pass("设置包含工作台分组（默认）",
            settings?.Workbench is { Rows: 6, Columns: 5, CardWidth: 240, CardHeight: 200, MaxEmergencyTasks: 3 },
            settings?.Workbench?.ToString() ?? "null");

        var put = await admin.PutAsJsonAsync("/api/v1/settings/workbench", new
        {
            group = "workbench",
            values = new Dictionary<string, string>
            {
                ["rows"] = "4",
                ["columns"] = "6",
                ["cardWidth"] = "260",
                ["cardHeight"] = "210",
                ["maxEmergencyTasks"] = "2"
            }
        });
        var after = (await admin.GetFromJsonAsync<Resp<SettingsData>>("/api/v1/settings"))?.Data;
        Pass("工作台显示设置修改生效（含紧急上限）",
            put.IsSuccessStatusCode &&
            after?.Workbench is { Rows: 4, Columns: 6, CardWidth: 260, CardHeight: 210, MaxEmergencyTasks: 2 },
            after?.Workbench?.ToString() ?? "null");
    }

    var runtimeJson = new RuntimeSettingsFile().ReadJson() ?? string.Empty;
    Pass("运行时文件含工作台配置", runtimeJson.Contains("Workbench") && runtimeJson.Contains("MaxEmergencyTasks"),
        $"len={runtimeJson.Length}");

    // ---------- 5. 紧急优先上限（服务层强制） ----------
    long id1, id2, id3;
    using (var db = new SqlSugar.SqlSugarClient(new SqlSugar.ConnectionConfig
    {
        ConnectionString = $"Data Source={Path.Combine(dbDir, "station.db")}",
        DbType = SqlSugar.DbType.Sqlite,
        IsAutoCloseConnection = true
    }))
    {
        var gen = new SnowflakeIdGenerator();
        id1 = gen.NextId();
        id2 = gen.NextId();
        id3 = gen.NextId();
        var now = DateTime.Now;
        db.Insertable(new CollectTask
        {
            Id = id1, TaskNo = "EM-1", RecorderName = "紧急测试1", Status = CollectTaskStatus.Collecting,
            IsAuto = true, CreatedAt = now
        }).ExecuteCommand();
        db.Insertable(new CollectTask
        {
            Id = id2, TaskNo = "EM-2", RecorderName = "紧急测试2", Status = CollectTaskStatus.Collecting,
            IsAuto = true, CreatedAt = now
        }).ExecuteCommand();
        db.Insertable(new CollectTask
        {
            Id = id3, TaskNo = "EM-3", RecorderName = "紧急测试3", Status = CollectTaskStatus.Collecting,
            IsAuto = true, CreatedAt = now
        }).ExecuteCommand();
    }

    var collect = host.Services.GetRequiredService<ICollectTaskService>();
    var e1 = await collect.SetEmergencyAsync(id1, true);
    var e2 = await collect.SetEmergencyAsync(id2, true);
    var e3Blocked = await collect.SetEmergencyAsync(id3, true);
    var e2Off = await collect.SetEmergencyAsync(id2, false);
    var e3On = await collect.SetEmergencyAsync(id3, true);
    Pass("紧急优先上限控制",
        e1 && e2 && !e3Blocked && e2Off && e3On,
        $"e1={e1}, e2={e2}, e3(上限2)={e3Blocked}, 取消后重标={e3On}");

    var active = await collect.GetActiveTasksAsync();
    Pass("任务 DTO 含紧急标记",
        active.Any(t => t.TaskId == id1 && t.IsEmergency) &&
        active.Any(t => t.TaskId == id3 && t.IsEmergency),
        $"emergency={active.Count(t => t.IsEmergency)}");

    await host.StopAsync();
}

try
{
    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}
catch
{
    // 清理失败不影响结果
}

Console.WriteLine();
Console.WriteLine("================ 桌面端审计修复验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed class FakeDetector : IRecorderDeviceDetector
{
    public string Protocol => "ums";

    public List<DetectedDevice> Current { get; } = [];

    public IReadOnlyList<DetectedDevice> Detect() => Current.ToList();
}

internal sealed class FakeMtpDetector : IRecorderDeviceDetector
{
    public string Protocol => "mtp";

    public List<DetectedDevice> Current { get; } = [];

    public IReadOnlyList<DetectedDevice> Detect() => Current.ToList();
}

internal sealed record Resp<T>(bool Success, int Code, string Message, T? Data);

internal sealed record SettingsData(
    object? Basic,
    object? Storage,
    object? Collect,
    WorkbenchSettingsDto? Workbench,
    object? License,
    object? Network,
    bool ReadOnly);
