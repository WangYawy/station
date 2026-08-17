using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Application;
using Station.Application.Collecting;
using Station.Contracts;

// ---------------------------------------------------------------------------
// M33 Spike: UMS 采集源（真机就绪）
//  - 覆盖目录模式下：扫描/复制/擦除与真实 U 盘同一套逻辑
//  - 无设备（无覆盖目录、无可移动磁盘）时抛出明确错误
//  - DI 按 SourceMode 选择采集源
// ---------------------------------------------------------------------------

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var root = Path.Combine(Path.GetTempPath(), "station-m33-ums-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var dest = Path.Combine(Path.GetTempPath(), "station-m33-ums-dest-" + Guid.NewGuid().ToString("N"));

try
{
    var options = new CollectOptions { SourceMode = "ums", UmsRootOverride = root, ChunkBytes = 64 * 1024 };
    var source = new UmsCollectSource(options);

    // ---------- 1. 扫描 ----------
    try
    {
        File.WriteAllBytes(Path.Combine(root, "video_1.mp4"), new byte[1024 * 1024]);
        File.WriteAllText(Path.Combine(root, "notes.log"), "ums log\n");
        var files = await source.ScanAsync(new CollectDeviceInfo("U盘", "USB-001", ProtocolType.Ums), CancellationToken.None);
        Pass("UMS扫描", files.Count == 2 &&
                          files.Any(f => f.FileName == "video_1.mp4" && f.Size == 1024 * 1024) &&
                          files.Any(f => f.FileName == "notes.log"),
            $"文件={string.Join(",", files.Select(f => $"{f.FileName}:{f.Size}"))}");
    }
    catch (Exception ex)
    {
        Pass("UMS扫描", false, ex.Message);
    }

    // ---------- 2. 根目录解析（覆盖目录） ----------
    try
    {
        var resolved = source.GetRecorderRoot(new CollectDeviceInfo("U盘", "USB-001", ProtocolType.Ums));
        Pass("UMS根目录解析", resolved == root, $"root={resolved}");
    }
    catch (Exception ex)
    {
        Pass("UMS根目录解析", false, ex.Message);
    }

    // ---------- 3. 复制 ----------
    try
    {
        var files = await source.ScanAsync(new CollectDeviceInfo("U盘", "USB-001", ProtocolType.Ums), CancellationToken.None);
        var video = files.First(f => f.FileName == "video_1.mp4");
        await source.CopyAsync(
            new CollectDeviceInfo("U盘", "USB-001", ProtocolType.Ums, RootPath: root),
            video,
            Path.Combine(dest, "copy.mp4"),
            null,
            CancellationToken.None);
        Pass("UMS复制", new FileInfo(Path.Combine(dest, "copy.mp4")).Length == 1024 * 1024,
            $"目标大小={new FileInfo(Path.Combine(dest, "copy.mp4")).Length}");
    }
    catch (Exception ex)
    {
        Pass("UMS复制", false, ex.Message);
    }

    // ---------- 4. 擦除（普通删除） ----------
    try
    {
        await source.EraseAsync(new CollectDeviceInfo("U盘", "USB-001", ProtocolType.Ums), CancellationToken.None);
        Pass("UMS擦除", Directory.GetFiles(root).Length == 0, $"剩余文件={Directory.GetFiles(root).Length}");
    }
    catch (Exception ex)
    {
        Pass("UMS擦除", false, ex.Message);
    }

    // ---------- 5. 无设备报错 ----------
    try
    {
        var noDevice = new UmsCollectSource(new CollectOptions { SourceMode = "ums" });
        _ = noDevice.GetRecorderRoot(new CollectDeviceInfo("U盘", "USB-001", ProtocolType.Ums));
        Pass("无设备报错", false, "未抛错");
    }
    catch (InvalidOperationException ex)
    {
        Pass("无设备报错", ex.Message.Contains("未检测到 UMS 设备"), ex.Message);
    }
    catch (Exception ex)
    {
        Pass("无设备报错", false, ex.ToString());
    }

    // ---------- 6. DI 按 SourceMode 选择 ----------
    try
    {
        var umsConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Station:Collect:SourceMode"] = "ums",
                ["Station:Collect:UmsRootOverride"] = root,
                ["Station:Collect:CacheDirectory"] = Path.Combine(Path.GetTempPath(), "station-m33-cache")
            })
            .Build();
        var services = new ServiceCollection();
        services.AddStationApplication(umsConfig);
        await using var sp = services.BuildServiceProvider();
        var umsSource = sp.GetRequiredService<ICollectSource>();
        var simConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Station:Collect:SourceMode"] = "simulated",
                ["Station:Collect:CacheDirectory"] = Path.Combine(Path.GetTempPath(), "station-m33-cache")
            })
            .Build();
        var simServices = new ServiceCollection();
        simServices.AddStationApplication(simConfig);
        await using var simSp = simServices.BuildServiceProvider();
        var simSource = simSp.GetRequiredService<ICollectSource>();
        Pass("DI选择采集源", umsSource is UmsCollectSource && simSource is SimulatedCollectSource,
            $"ums={umsSource.GetType().Name}, simulated={simSource.GetType().Name}");
    }
    catch (Exception ex)
    {
        Pass("DI选择采集源", false, ex.ToString());
    }
}
finally
{
    try
    {
        Directory.Delete(root, true);
        Directory.Delete(dest, true);
    }
    catch
    {
    }
}

Console.WriteLine();
Console.WriteLine("================ M33 UMS 采集源（真机就绪） ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
