using Microsoft.Extensions.Hosting;
using Station.Application.Collecting;
using Station.Contracts;
using Station.Desktop.Bootstrapper;
using Station.Desktop.Infrastructure.Collecting;
using Station.Desktop.Infrastructure.Settings;
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

var mtpOnlyMonitor = new RecorderConnectMonitor(
    new CollectOptions { SourceMode = "mtp" },
    new IRecorderDeviceDetector[] { fake }, // 只有 UMS 检测器
    d => { handled.Add("mtp:" + d.Key); return Task.CompletedTask; },
    settlePolls: 1);
await mtpOnlyMonitor.CheckAsync();
Pass("协议不匹配不触发", handled.Count == 0, $"handled={handled.Count}");

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

using (var host = HostBuilderFactory.Create().Build())
{
    await host.StartAsync();
    await Task.Delay(1500);
    var dbFile = Path.Combine(dbDir, "station.db");
    Pass("宿主启动后 DB 落在数据目录", File.Exists(dbFile), dbFile);
    Pass("运行时设置文件路径为数据目录",
        RuntimeSettingsFile.ResolvePath() == Path.Combine(dbDir, "appsettings.runtime.json"),
        RuntimeSettingsFile.ResolvePath());
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
