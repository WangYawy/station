using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Application;
using Station.Application.Collecting;
using Station.Desktop.Infrastructure;
using Station.Desktop.Infrastructure.Collecting;
using Station.Desktop.Infrastructure.Recorders;
using Station.Infrastructure.Recorders;

// ---------------------------------------------------------------------------
// MTP 采集源 spike：无真机环境验证设备缺失路径、虚拟根目录、绑定文件存取分发与 DI 选源。
// 真机接入后（SourceMode=mtp）同一代码路径即走 WPD 扫描/复制/擦除。
// ---------------------------------------------------------------------------

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var options = new CollectOptions { SourceMode = "mtp" };
var source = new MtpCollectSource(options);

// ---------- 1. 源标识与平台 ----------
Pass("源标识", source.SourceKey == "mtp", $"SourceKey={source.SourceKey}");
Pass("Windows 判定", !OperatingSystem.IsWindows() || source.GetType().FullName?.Contains("MtpCollectSource") == true,
    $"OS={Environment.OSVersion.VersionString}");

// ---------- 2. 无设备路径（Windows）或平台不支持（非 Windows） ----------
try
{
    var root = source.GetRecorderRoot(new CollectDeviceInfo("MTP-测试", "SN-MTP-001", Station.Contracts.ProtocolType.Mtp));
    Pass("虚拟根目录", root.StartsWith("MTP://"), root);
}
catch (InvalidOperationException ex)
{
    Pass("无 MTP 设备明确报错", ex.Message.Contains("未检测到 MTP 设备"), ex.Message);
}
catch (PlatformNotSupportedException ex)
{
    Pass("非 Windows 平台明确报错", ex.Message.Contains("仅支持 Windows"), ex.Message);
}

try
{
    var files = await source.ScanAsync(
        new CollectDeviceInfo("MTP-测试", "SN-MTP-001", Station.Contracts.ProtocolType.Mtp),
        CancellationToken.None);
    Pass("扫描（无设备不返回空）", false, $"意外返回 {files.Count} 个文件");
}
catch (InvalidOperationException ex)
{
    Pass("扫描无设备明确报错", ex.Message.Contains("未检测到 MTP 设备"), ex.Message);
}
catch (PlatformNotSupportedException ex)
{
    Pass("扫描非 Windows 明确报错", ex.Message.Contains("仅支持 Windows"), ex.Message);
}

try
{
    var store = (IRecorderRootFileStore)source;
    var content = store.ReadFile("MTP://SWD\\WPDBUSENUM\\xxx", "station_bind.ini");
    Pass("MTP 绑定文件读取（无设备）", false, $"意外返回 {content?.Length ?? 0} 字符");
}
catch (InvalidOperationException ex)
{
    Pass("MTP 绑定读取无设备明确报错", ex.Message.Contains("未检测到 MTP 设备"), ex.Message);
}
catch (PlatformNotSupportedException ex)
{
    Pass("绑定读取非 Windows 明确报错", ex.Message.Contains("仅支持 Windows"), ex.Message);
}

// ---------- 3. DI 选源：mtp -> MtpCollectSource（Windows）/ 明确报错（非 Windows） ----------
try
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Station:Collect:SourceMode"] = "mtp"
        })
        .Build();
    var services = new ServiceCollection();
    services.AddStationApplication(configuration);
    services.AddInfrastructure(configuration);
    var provider = services.BuildServiceProvider();
    var resolved = provider.GetRequiredService<ICollectSource>();
    Pass("DI 选择 MTP 采集源", resolved is MtpCollectSource, resolved.GetType().Name);

    var rootStore = provider.GetRequiredService<IRecorderRootFileStore>();
    Pass("DI 根文件存取为 MTP 复合实现", rootStore is CompositeRecorderRootFileStore, rootStore.GetType().Name);
}
catch (PlatformNotSupportedException ex)
{
    Pass("DI 非 Windows 明确报错", ex.Message.Contains("仅支持 Windows"), ex.Message);
}

// ---------- 4. 绑定文件存取分发：普通路径走文件系统 ----------
var tempRoot = Path.Combine(Path.GetTempPath(), "station-mtp-spike-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(tempRoot);
    var store = new CompositeRecorderRootFileStore(source);
    store.WriteFile(tempRoot, "station_bind.ini", "recorder_serial=SN-MTP-001");
    var readBack = store.ReadFile(tempRoot, "station_bind.ini");
    Pass("普通根目录绑定文件读写（文件系统）", readBack == "recorder_serial=SN-MTP-001", $"read={readBack}");
    store.DeleteFile(tempRoot, "station_bind.ini");
    Pass("普通根目录绑定文件删除", store.ReadFile(tempRoot, "station_bind.ini") is null, "已删除");
}
finally
{
    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}

Console.WriteLine();
Console.WriteLine("================ MTP 采集源验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;
