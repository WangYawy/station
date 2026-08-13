using Station.Application.Collecting;
using Station.Desktop.Application.Monitoring;

// ---------------------------------------------------------------------------
// M43 Spike: 桌面端状态监控（CPU/内存/磁盘/网络/端口/设备）
// ---------------------------------------------------------------------------

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var monitor = new SystemMonitorService(new CollectOptions());

// ---------- 1. 首次快照（网络需二次采样） ----------
try
{
    var first = monitor.Snapshot().ToList();
    Pass("首次快照", first.Count == 6 &&
                     first.Any(l => l.Label == "CPU") &&
                     first.Any(l => l.Label == "内存") &&
                     first.Any(l => l.Label == "磁盘") &&
                     first.Any(l => l.Label == "设备"),
        string.Join(" | ", first.Select(l => $"{l.Label}={l.Value}")));
}
catch (Exception ex)
{
    Pass("首次快照", false, ex.ToString());
}

// ---------- 2. 二次采样：CPU/网络出数值（Windows 下） ----------
try
{
    await Task.Delay(1100);
    var second = monitor.Snapshot().ToList();
    var cpu = second.First(l => l.Label == "CPU").Value;
    var network = second.First(l => l.Label == "网络").Value;
    var ports = second.First(l => l.Label == "监听端口").Value;
    Pass("二次采样", OperatingSystem.IsWindows()
        ? cpu != "—" && network != "—" && ports != "—"
        : second.Count == 6,
        string.Join(" | ", second.Select(l => $"{l.Label}={l.Value}")));
}
catch (Exception ex)
{
    Pass("二次采样", false, ex.ToString());
}

Console.WriteLine();
Console.WriteLine("================ M43 状态监控 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
