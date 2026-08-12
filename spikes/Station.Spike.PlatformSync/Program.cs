using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Station.Application;
using Station.Application.PlatformSync;
using Station.Contracts.Alerts;
using Station.Contracts.Registration;
using Station.Contracts.Reporting;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Repositories;

// ---------------------------------------------------------------------------
// M11 Spike: 站↔平台通信（注册/元数据/报警上报、指令轮询回执、断网补报）
// ---------------------------------------------------------------------------

var dbArg = "sqlite";
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--db" && i + 1 < args.Length) dbArg = args[i + 1];
}

var provider = dbArg.ToLowerInvariant() switch
{
    "kingbase" => DbProvider.Kingbase,
    "mysql" => DbProvider.MySql,
    "postgresql" or "pg" => DbProvider.PostgreSQL,
    _ => DbProvider.Sqlite
};
var connStr = provider switch
{
    DbProvider.Kingbase => "Host=localhost;Port=54321;Database=test;Username=system;Password=Test@123",
    DbProvider.MySql => "Server=localhost;Port=3306;Database=station_spike;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
    DbProvider.PostgreSQL => "Host=localhost;Port=5432;Database=station_spike;Username=station;Password=Station@123",
    _ => $"Data Source={Path.Combine(AppContext.BaseDirectory, "spike_platformsync.db")}"
};

const int Port = 5199;
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var fake = new FakePlatform(Port);
fake.Start();

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Station:Db:Provider"] = provider.ToString(),
        ["Station:Db:ConnectionString"] = connStr,
        ["Station:Platform:Enabled"] = "true",
        ["Station:Platform:BaseUrl"] = $"http://127.0.0.1:{Port}",
        ["Station:Platform:StationCode"] = "ST0001",
        ["Station:Platform:SyncIntervalSeconds"] = "5",
        ["Station:Platform:CommandPollIntervalSeconds"] = "5",
        ["Station:Platform:TimeoutSeconds"] = "10"
    })
    .Build();

var services = new ServiceCollection();
services.AddStationDatabase(config);
services.AddStationApplication(config);
await using var sp = services.BuildServiceProvider();

sp.GetRequiredService<IDatabaseInitializer>().EnsureCreated(typeof(SyncOutbox));
var outbox = sp.GetRequiredService<ISyncOutboxService>();
var commands = sp.GetRequiredService<ICommandService>();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// ---------- 1. 注册上报 ----------
try
{
    var register = new StationRegistrationRequest
    {
        StationCode = "ST0001",
        MachineFingerprint = new MachineFingerprint
        {
            CpuSerial = "cpu-1",
            MotherboardSerial = "mb-1",
            DiskSerial = "disk-1",
            MacAddress = "aa:bb:cc:dd:ee:ff"
        },
        OsVersion = "windows-11",
        CpuArch = "x86_64",
        SoftwareVersion = "0.1.0"
    };
    await outbox.EnqueueAsync("register", JsonSerializer.Serialize(register, json));
    var sent = await outbox.DrainAsync();
    Pass("注册上报", sent == 1 && fake.RegisterCount == 1,
        $"上报成功={sent}, 平台收到注册={fake.RegisterCount}");
}
catch (Exception ex)
{
    Pass("注册上报", false, ex.Message);
}

// ---------- 2. 元数据 + 报警上报 ----------
try
{
    var metadata = new FileMetadataReport
    {
        StationId = 1,
        LocalFileId = 1,
        FileNo = "ST0001-1",
        FileName = "video_1.mp4",
        Size = 100,
        Kind = Station.Contracts.FileKind.Video,
        Sm3 = "abc",
        CollectedAt = DateTime.Now,
        RecorderSerial = "SIM-R1"
    };
    await outbox.EnqueueAsync("file-metadata", JsonSerializer.Serialize(metadata, json));

    var alert = new AlertReport
    {
        StationId = 1,
        LocalAlertId = 1,
        Type = Station.Contracts.AlertType.UnauthorizedAccess,
        Level = Station.Contracts.AlertLevel.Critical,
        Source = "SIM-ROGUE",
        Message = "非授权接入",
        OccurredAt = DateTime.Now
    };
    await outbox.EnqueueAsync("alert", JsonSerializer.Serialize(alert, json));

    var sent = await outbox.DrainAsync();
    Pass("元数据+报警上报", sent == 2 && fake.MetadataCount == 1 && fake.AlertCount == 1,
        $"上报成功={sent}, 平台元数据={fake.MetadataCount}, 平台报警={fake.AlertCount}");
}
catch (Exception ex)
{
    Pass("元数据+报警上报", false, ex.Message);
}

// ---------- 3. 指令轮询 → 执行 → 回执 ----------
try
{
    var pulled = await commands.PollAndExecuteAsync(1);
    var sent = await outbox.DrainAsync();
    Pass("指令轮询回执", pulled == 1 && sent == 1 && fake.CommandResultCount == 1,
        $"拉取指令={pulled}, 回执上报={sent}, 平台收到回执={fake.CommandResultCount}");
}
catch (Exception ex)
{
    Pass("指令轮询回执", false, ex.Message);
}

// ---------- 4. 断网补报 ----------
try
{
    var alert2 = new AlertReport
    {
        StationId = 1,
        LocalAlertId = 2,
        Type = Station.Contracts.AlertType.BindingInvalid,
        Level = Station.Contracts.AlertLevel.Warning,
        Source = "SIM-R1",
        Message = "绑定异常（断网期间产生）",
        OccurredAt = DateTime.Now
    };
    await outbox.EnqueueAsync("alert", JsonSerializer.Serialize(alert2, json));

    fake.Stop(); // 断网
    var failedSent = await outbox.DrainAsync();
    var pendingWhileDown = await outbox.CountPendingAsync();
    var retried = (await sp.GetRequiredService<IRepository<SyncOutbox>>().GetListAsync(o => o.Status == 0)).First().RetryCount;

    fake.Start(); // 恢复
    // 模拟"网络恢复事件"触发重试：清退避时间（产品中由同步 Worker 按轮次自然到达）
    var pendingItem = (await sp.GetRequiredService<IRepository<SyncOutbox>>().GetListAsync(o => o.Status == 0)).First();
    pendingItem.NextRetryAt = null;
    await sp.GetRequiredService<IRepository<SyncOutbox>>().UpdateAsync(pendingItem);
    var recoveredSent = await outbox.DrainAsync();
    var pendingAfter = await outbox.CountPendingAsync();

    Pass("断网补报", failedSent == 0 && pendingWhileDown == 1 && retried == 1 && recoveredSent == 1 && pendingAfter == 0 && fake.AlertCount == 2,
        $"断网失败={failedSent}, 队列滞留={pendingWhileDown}, 重试计数={retried}, 恢复补报={recoveredSent}, 平台报警总数={fake.AlertCount}");
}
catch (Exception ex)
{
    Pass("断网补报", false, ex.Message);
}

// ---------- 5. 清理 ----------
try
{
    var db = sp.GetRequiredService<ISqlSugarClient>();
    db.DbMaintenance.DropTable<SyncOutbox>();
    fake.Dispose();
    Pass("清理", true, "已删除测试表并关闭模拟平台");
}
catch (Exception ex)
{
    Pass("清理", false, ex.Message);
}

Console.WriteLine();
Console.WriteLine($"================ {provider} 站↔平台通信验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed class FakePlatform : IDisposable
{
    private readonly HttpListener _listener;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public int RegisterCount;
    public int MetadataCount;
    public int AlertCount;
    public int CommandResultCount;
    public bool GiveCommand = true;

    public FakePlatform(int port)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        _ = LoopAsync();
    }

    public void Stop()
    {
        _listener.Stop();
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleAsync(context);
            }
            catch
            {
                return;
            }
        }
    }

    private Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url!.AbsolutePath;
        var method = context.Request.HttpMethod;

        if (path.EndsWith("/stations/register") && method == "POST")
        {
            RegisterCount++;
            Write(context, new { success = true, code = 0, message = "ok", data = new { stationId = 1L, stationCode = "ST0001", licenseStatus = 0, isRegistered = true, configVersion = 0L } });
        }
        else if (path.EndsWith("/files/metadata") && method == "POST")
        {
            MetadataCount++;
            Write(context, new { success = true, code = 0, message = "ok", data = new { accepted = true, duplicate = false } });
        }
        else if (path.EndsWith("/alerts") && method == "POST")
        {
            AlertCount++;
            Write(context, new { success = true, code = 0, message = "ok", data = true });
        }
        else if (path.EndsWith("/commands/poll") && method == "GET")
        {
            var list = new List<object>();
            if (GiveCommand)
            {
                list.Add(new
                {
                    commandId = 101L,
                    stationId = 1L,
                    type = 0,
                    payloadJson = (string?)null,
                    issuedAt = DateTime.Now,
                    timeoutSeconds = 300,
                    signature = "test-signature"
                });
                GiveCommand = false;
            }

            Write(context, new { success = true, code = 0, message = "ok", data = list });
        }
        else if (path.EndsWith("/result") && method == "POST")
        {
            CommandResultCount++;
            Write(context, new { success = true, code = 0, message = "ok", data = true });
        }
        else if (path.EndsWith("/config-sync") && method == "POST")
        {
            Write(context, new { success = true, code = 0, message = "ok", data = new { version = 1L, changes = Array.Empty<object>() } });
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
        }

        return Task.CompletedTask;
    }

    private void Write(HttpListenerContext context, object body)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, _json));
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = 200;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }
}
