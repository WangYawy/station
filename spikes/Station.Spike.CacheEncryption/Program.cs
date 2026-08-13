using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.Collecting;
using Station.Application.PlatformSync;
using Station.Application.Uploading;
using Station.Contracts;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Security;
using Station.Infrastructure.Storage;

// ---------------------------------------------------------------------------
// M40 Spike: 缓存 SM4 加密全链路（采集加密 → 台账SM3 → 上传解密 → 预览Range）
// ---------------------------------------------------------------------------

const int WebPort = 5127;
Environment.CurrentDirectory = @"E:\Reny\station\src\Station.Desktop\Station.Desktop.WebHost";
var testRoot = Path.Combine(Path.GetTempPath(), "station-m40-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var dbFile = Path.Combine(testRoot, "station.db");
var cacheDir = Path.Combine(testRoot, "cache");
var simDir = Path.Combine(testRoot, "sim");
var remoteRoot = Path.Combine(testRoot, "remote");

Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", cacheDir);
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", simDir);
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDFILECOUNT", "2");
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDCHUNKDELAYMS", "0");
Environment.SetEnvironmentVariable("STATION__COLLECT__ENCRYPTCACHE", "true");
Environment.SetEnvironmentVariable("STATION__COLLECT__AUTOCOLLECTONCONNECT", "true");
Environment.SetEnvironmentVariable("STATION__STORAGE__TARGET", "Local");
Environment.SetEnvironmentVariable("STATION__STORAGE__LOCALROOT", remoteRoot);
Environment.SetEnvironmentVariable("STATION__STORAGE__STATIONNO", "ST0001");
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");
Environment.SetEnvironmentVariable("STATION__WEB__PORT", WebPort.ToString());
Environment.SetEnvironmentVariable("STATION__WEB__ENABLELAN", "false");

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

using var host = HostBuilderFactory.Create().Build();
await host.StartAsync();

var deadline = DateTime.Now.AddSeconds(30);
while (DateTime.Now < deadline)
{
    try
    {
        using var probe = new TcpClient();
        probe.Connect("127.0.0.1", WebPort);
        break;
    }
    catch
    {
        await Task.Delay(300);
    }
}

try
{
    var collect = host.Services.GetRequiredService<ICollectTaskService>();
    var upload = host.Services.GetRequiredService<IUploadService>();
    var ledger = host.Services.GetRequiredService<IFileLedgerService>();
    var db = host.Services.GetRequiredService<ISqlSugarClient>();

    var task = await collect.CreateTaskAsync(new CollectDeviceInfo("记录仪-M40", "SIM-M40", Station.Contracts.ProtocolType.Ums), isAuto: true);
    var waitDeadline = DateTime.Now.AddSeconds(90);
    while (DateTime.Now < waitDeadline)
    {
        var current = await collect.GetTaskAsync(task.TaskId);
        if (current is not null && current.Status is
            CollectTaskStatus.Completed or CollectTaskStatus.Failed or CollectTaskStatus.Interrupted)
        {
            break;
        }

        await Task.Delay(200);
    }

    var files = db.Queryable<CollectFile>().Where(f => f.TaskId == task.TaskId).ToList();
    var taskState = await collect.GetTaskAsync(task.TaskId);
    Console.WriteLine($"[DIAG] 任务状态={taskState?.Status}, 文件数={files.Count}, 文件名={string.Join(",", files.Select(f => f.FileName))}, 扩展={string.Join(",", files.Select(f => f.Extension))}");
    var video = files.First(f => f.Extension.TrimStart('.') == "mp4");
    var sourcePath = Path.Combine(simDir, video.FileName);
    var cachePath = Path.Combine(cacheDir, task.TaskNo, video.RelativePath);
    var sourceBytes = File.ReadAllBytes(sourcePath);

    // ---------- 1. 缓存文件已加密（+16 字节头、首块非明文） ----------
    try
    {
        var cacheBytes = File.ReadAllBytes(cachePath);
        var encrypted = cacheBytes.Length == sourceBytes.Length + 16 &&
                        !cacheBytes.AsSpan(16, Math.Min(16, sourceBytes.Length)).SequenceEqual(sourceBytes.AsSpan(0, Math.Min(16, sourceBytes.Length)));
        Pass("缓存SM4加密", encrypted, $"缓存大小={cacheBytes.Length}, 明文大小={sourceBytes.Length}");
    }
    catch (Exception ex)
    {
        Pass("缓存SM4加密", false, ex.Message);
    }

    // ---------- 2. 台账 SM3 = 明文 SM3 ----------
    try
    {
        await ledger.ProcessCompletedTaskAsync(task.TaskId);
        var after = db.Queryable<CollectFile>().Where(f => f.TaskId == task.TaskId).ToList();
        var sm3 = after.First(f => f.FileName == video.FileName).Sm3;
        var expected = Sm3Checksum.ComputeFile(sourcePath);
        Pass("台账SM3为明文", sm3 == expected, $"sm3={sm3}");
    }
    catch (Exception ex)
    {
        Pass("台账SM3为明文", false, ex.ToString());
    }

    // ---------- 3. 上传解密：远端文件 = 明文 ----------
    try
    {
        var summary = await upload.ProcessTaskAsync(task.TaskId);
        var after = db.Queryable<CollectFile>().Where(f => f.TaskId == task.TaskId).ToList();
        var uploaded = after.First(f => f.FileName == video.FileName);
        var remotePath = Path.Combine(remoteRoot, uploaded.RemotePath!.Replace('/', Path.DirectorySeparatorChar));
        var remoteBytes = File.ReadAllBytes(remotePath);
        Pass("上传解密", summary.Uploaded == 2 && remoteBytes.SequenceEqual(sourceBytes),
            $"上传={summary.Uploaded}/2, 远端一致={remoteBytes.SequenceEqual(sourceBytes)}");
    }
    catch (Exception ex)
    {
        Pass("上传解密", false, ex.ToString());
    }

    // ---------- 4. 预览 Range：解密流按偏移返回明文切片 ----------
    try
    {
        var after = db.Queryable<CollectFile>().Where(f => f.TaskId == task.TaskId).ToList();
        var uploaded = after.First(f => f.FileName == video.FileName);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{WebPort}") };
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/{uploaded.FileNo}/stream");
        req.Headers.TryAddWithoutValidation("Range", "bytes=100-");
        var resp = await http.SendAsync(req);
        var body = await resp.Content.ReadAsByteArrayAsync();
        var slice = sourceBytes.Skip(100).ToArray();
        Pass("预览Range解密", resp.StatusCode == HttpStatusCode.PartialContent &&
                             body.Length == slice.Length && body.SequenceEqual(slice),
            $"HTTP {(int)resp.StatusCode}, 长度={body.Length}, 期望={slice.Length}");
    }
    catch (Exception ex)
    {
        Pass("预览Range解密", false, ex.ToString());
    }
}
finally
{
    await host.StopAsync();
    host.Dispose();
    try
    {
        Directory.Delete(testRoot, true);
    }
    catch
    {
    }
}

Console.WriteLine();
Console.WriteLine("================ M40 缓存 SM4 加密全链路 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
