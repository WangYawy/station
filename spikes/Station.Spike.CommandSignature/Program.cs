using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Contracts.Commands;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;

// ---------------------------------------------------------------------------
// M18 Spike: 远程指令 SM2 签名/验签（有效执行 / 篡改签名拒绝执行并回执 Failed）
// ---------------------------------------------------------------------------

var testRoot = @"E:\Reny\station\archive\commandsig-test";
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

var (privatePem, publicPem) = Sm2LicenseSigner.CreateKeyPair();

Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(testRoot, "cache"));
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", Path.Combine(testRoot, "sim"));
Environment.SetEnvironmentVariable("STATION__PLATFORM__ENABLED", "true");
Environment.SetEnvironmentVariable("STATION__PLATFORM__BASEURL", "http://127.0.0.1:5105");
Environment.SetEnvironmentVariable("STATION__PLATFORM__STATIONCODE", "ST-SIG");
Environment.SetEnvironmentVariable("STATION__COMMAND__PUBLICKEYPEM", publicPem);
Environment.SetEnvironmentVariable("STATION__COMMAND__REQUIRED", "true");
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");

const int PlatformPort = 5105;
var privateKeyFile = Path.Combine(testRoot, "platform-private.pem");
File.WriteAllText(privateKeyFile, privatePem);
using (var clean = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    clean.Ado.ExecuteCommand("delete from platform_command");
}
var platformExe = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api\bin\Release\net8.0\Station.Platform.Api.exe";
var platform = Process.Start(new ProcessStartInfo
{
    FileName = platformExe,
    Arguments = $"--urls http://127.0.0.1:{PlatformPort} --Platform:Command:PrivateKeyPemFile={privateKeyFile}",
    WorkingDirectory = Path.GetDirectoryName(platformExe)!,
    UseShellExecute = false,
    CreateNoWindow = true,
    Environment =
    {
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{PlatformPort}",
        ["STATION__DB__PROVIDER"] = "MySql",
        ["STATION__DB__CONNECTIONSTRING"] = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None"
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

    var context = host.Services.GetRequiredService<IStationContext>();
    var registerDeadline = DateTime.Now.AddSeconds(30);
    while (context.StationId is null && DateTime.Now < registerDeadline)
    {
        await Task.Delay(300);
    }

    var stationId = context.StationId!.Value;
    using var http = new HttpClient();

    // ---------- 1. 有效签名指令：验签通过 → 执行成功 ----------
    try
    {
        var resp = await http.PostAsJsonAsync(
            $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/commands",
            new { type = (int)CommandType.RunSelfCheck });
        resp.EnsureSuccessStatusCode();
        var dispatch = await resp.Content.ReadFromJsonAsync<DispatchResponse>();
        var commandId = dispatch!.Data!.CommandId;

        var commands = host.Services.GetRequiredService<ICommandService>();
        var outbox = host.Services.GetRequiredService<ISyncOutboxService>();
        await commands.PollAndExecuteAsync(stationId);
        await outbox.DrainAsync();

        var list = await http.GetFromJsonAsync<CommandListResponse>(
            $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/commands");
        var executed = list!.Data!.First(c => c.CommandId == commandId);
        Pass("有效签名执行", executed.Status == CommandStatus.Succeeded && executed.Message!.Contains("自检"),
            $"状态={executed.Status}, 消息={executed.Message}");
    }
    catch (Exception ex)
    {
        Pass("有效签名执行", false, ex.Message);
    }

    // ---------- 2. 篡改签名：验签失败 → 拒绝执行 + 回执 Failed ----------
    try
    {
        var resp = await http.PostAsJsonAsync(
            $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/commands",
            new { type = (int)CommandType.ClearCache });
        resp.EnsureSuccessStatusCode();
        var dispatch = await resp.Content.ReadFromJsonAsync<DispatchResponse>();
        var commandId = dispatch!.Data!.CommandId;

        // 平台库篡改签名
        var platformDb = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        });
        platformDb.Ado.ExecuteCommand(
            "update platform_command set signature='tampered' where id=@id",
            new { id = commandId });
        platformDb.Dispose();

        var commands = host.Services.GetRequiredService<ICommandService>();
        var outbox = host.Services.GetRequiredService<ISyncOutboxService>();
        await commands.PollAndExecuteAsync(stationId);
        await outbox.DrainAsync();

        var list = await http.GetFromJsonAsync<CommandListResponse>(
            $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/commands");
        var rejected = list!.Data!.First(c => c.CommandId == commandId);
        Pass("篡改签名拒绝", rejected.Status == CommandStatus.Failed && rejected.Message!.Contains("签名无效"),
            $"状态={rejected.Status}, 消息={rejected.Message}");
    }
    catch (Exception ex)
    {
        Pass("篡改签名拒绝", false, ex.Message);
    }

    // ---------- 3. 清理 ----------
    try
    {
        var db = host.Services.GetRequiredService<ISqlSugarClient>();
        db.DbMaintenance.DropTable<SyncOutbox>();
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
Console.WriteLine("================ 指令 SM2 签名验签验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record DispatchResponse(bool Success, int Code, string Message, DispatchData? Data);

internal sealed record DispatchData(long CommandId, int Status, string Message);

internal sealed record CommandListResponse(bool Success, int Code, string Message, List<CommandView>? Data);

internal sealed record CommandView(long CommandId, int Type, CommandStatus Status, DateTime IssuedAt, DateTime? FinishedAt, string? Message);
