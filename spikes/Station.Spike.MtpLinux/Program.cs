using Station.Application.Collecting;
using Station.Contracts;
using Station.Desktop.Infrastructure.Collecting;

// ---------------------------------------------------------------------------
// M56 Spike: Linux 真实 MTP 采集源（libmtp）
// 在 Linux 目标机执行（本项目在 WSL/信创 Linux 上运行）：
//   dotnet publish spikes/Station.Spike.MtpLinux -c Release -r linux-x64 --self-contained true
//   然后运行 publish 产物。
// 无真机时验证：libmtp 加载、无设备明确报错、检测器/Provider 路由正确。
// ---------------------------------------------------------------------------

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

if (!OperatingSystem.IsLinux())
{
    Console.WriteLine("本 spike 仅在 Linux 上执行（Windows 请用 Station.Spike.MtpSource）。");
    return 2;
}

var options = new CollectOptions { SourceMode = "mtp" };
var source = new LinuxMtpCollectSource(options);

// ---------- 1. 设备检测（libmtp 枚举；无设备返回空/无 libmtp 返回空） ----------
var detected = LinuxMtpCollectSource.DetectDevices(options);
Pass("libmtp 设备枚举不抛异常", detected is not null, $"devices={detected.Count}");

// ---------- 2. 无设备路径：明确报错（区分"未安装 libmtp"与"未检测到设备"） ----------
try
{
    _ = await source.ScanAsync(
        new CollectDeviceInfo("MTP-测试", "SN-MTP-L", ProtocolType.Mtp, RootPath: "MTP://SN-MTP-L"),
        CancellationToken.None);
    Pass("无设备扫描报错", false, "意外返回成功");
}
catch (Exception ex)
{
    var ok = ex.Message.Contains("未检测到 MTP 设备") || ex.Message.Contains("未安装 libmtp");
    Pass("无设备扫描明确报错", ok, ex.Message);
}

try
{
    _ = source.GetRecorderRoot(new CollectDeviceInfo("MTP-测试", "SN-MTP-L", ProtocolType.Mtp));
    Pass("无设备根目录报错", false, "意外返回成功");
}
catch (Exception ex)
{
    var ok = ex.Message.Contains("未检测到 MTP 设备") || ex.Message.Contains("未安装 libmtp");
    Pass("无设备根目录明确报错", ok, ex.Message);
}

try
{
    _ = ((Station.Infrastructure.Recorders.IRecorderRootFileStore)source)
        .ReadFile("MTP://SN-MTP-L", "station_bind.ini");
    Pass("无设备绑定读取报错", false, "意外返回 null/成功");
}
catch (Exception ex)
{
    var ok = ex.Message.Contains("未检测到 MTP 设备") || ex.Message.Contains("未安装 libmtp");
    Pass("无设备绑定读取明确报错", ok, ex.Message);
}

// ---------- 3. 检测器与 Provider 路由（Linux → libmtp 实现） ----------
var detector = new MtpDeviceDetector(options);
var detectorDevices = detector.Detect();
Pass("MTP 检测器走 libmtp（无设备为空）", detectorDevices.Count == 0, $"devices={detectorDevices.Count}");

var provider = new CollectSourceProvider(
    options,
    new UmsCollectSource(options),
    new SimulatedCollectSource(options),
    mtpLinux: source);
try
{
    var routed = provider.GetFor(ProtocolType.Mtp);
    Pass("Provider 路由 MTP → LinuxMtpCollectSource", routed is LinuxMtpCollectSource, routed.GetType().Name);
}
catch (Exception ex)
{
    Pass("Provider 路由 MTP → LinuxMtpCollectSource", false, ex.Message);
}

Console.WriteLine();
Console.WriteLine("================ Linux MTP 采集源验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;
