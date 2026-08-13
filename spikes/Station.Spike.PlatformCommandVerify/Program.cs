using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using SqlSugar;
using Station.Contracts;
using Station.Contracts.Commands;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M26 Spike: 远程指令 SM2 读取时实时验签
//  - 平台轮询读取时用自身公钥实时验签：有效→下发；篡改/未签名→拒绝下发并标记 Failed
//  - 指令下发接口补登录鉴权（未登录 401）
//  - 未配置密钥（开发模式）跳过验签兼容
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var (privatePem, publicPem) = Sm2LicenseSigner.CreateKeyPair();
var testRoot = Path.Combine(Path.GetTempPath(), "station-m26-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var privateKeyFile = Path.Combine(testRoot, "platform-private.pem");
File.WriteAllText(privateKeyFile, privatePem);

long stationId;
using (var seed = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    seed.Ado.ExecuteCommand("delete from platform_command");
    seed.Ado.ExecuteCommand("delete from platform_station where StationCode='ST-CMD'");
    var id = new SnowflakeIdGenerator();
    stationId = id.NextId();
    seed.Insertable(new PlatformStation
    {
        Id = stationId,
        StationCode = "ST-CMD",
        CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac",
        OsVersion = "win11", CpuArch = "x86_64", SoftwareVersion = "0.1.0",
        RegisteredAt = DateTime.Now
    }).ExecuteCommand();
}

static Process StartPlatform(int port, string? privateKeyFile, string mysql)
{
    var platformExe = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api\bin\Release\net8.0\Station.Platform.Api.exe";
    var args = $"--urls http://127.0.0.1:{port}";
    if (privateKeyFile is not null)
    {
        args += $" --Platform:Command:PrivateKeyPemFile={privateKeyFile}";
    }

    var process = Process.Start(new ProcessStartInfo
    {
        FileName = platformExe,
        Arguments = args,
        WorkingDirectory = Path.GetDirectoryName(platformExe)!,
        UseShellExecute = false,
        CreateNoWindow = true,
        Environment =
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
            ["STATION__DB__PROVIDER"] = "MySql",
            ["STATION__DB__CONNECTIONSTRING"] = mysql
        }
    })!;

    var deadline = DateTime.Now.AddSeconds(30);
    while (DateTime.Now < deadline)
    {
        try
        {
            using var probe = new TcpClient();
            probe.Connect("127.0.0.1", port);
            break;
        }
        catch
        {
            Thread.Sleep(500);
        }
    }

    return process;
}

static async Task<HttpClient> LoginAdmin(int port)
{
    var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
    var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = "Admin@123" });
    login.EnsureSuccessStatusCode();
    return http;
}

static async Task<long> Dispatch(HttpClient http, int port, long stationId, CommandType type)
{
    var resp = await http.PostAsJsonAsync(
        $"http://127.0.0.1:{port}/api/v1/stations/{stationId}/commands",
        new { type = (int)type });
    resp.EnsureSuccessStatusCode();
    var body = await resp.Content.ReadFromJsonAsync<DispatchResponse>();
    return body!.Data!.CommandId;
}

async Task<List<JsonElement>> Poll(HttpClient http, int port, long stationId)
{
    var resp = await http.GetAsync($"http://127.0.0.1:{port}/api/v1/stations/{stationId}/commands/poll");
    resp.EnsureSuccessStatusCode();
    var body = await resp.Content.ReadFromJsonAsync<PollResponse>();
    return body!.Data ?? [];
}

// ================= Phase A：配置密钥的平台（仅配私钥，公钥由平台推导） =================
const int KeyedPort = 5114;
var keyedPlatform = StartPlatform(KeyedPort, privateKeyFile, platformMysql);
try
{
    using var admin = await LoginAdmin(KeyedPort);

    // ---------- 0. 未登录下发 -> 401 ----------
    try
    {
        using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{KeyedPort}") };
        var resp = await anon.PostAsJsonAsync(
            $"http://127.0.0.1:{KeyedPort}/api/v1/stations/{stationId}/commands",
            new { type = (int)CommandType.RunSelfCheck });
        Pass("未登录下发401", resp.StatusCode == HttpStatusCode.Unauthorized, $"HTTP {(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("未登录下发401", false, ex.Message);
    }

    // ---------- 1. 有效签名指令：实时验签通过并下发 ----------
    long c1;
    try
    {
        c1 = await Dispatch(admin, KeyedPort, stationId, CommandType.RunSelfCheck);
        var delivered = await Poll(admin, KeyedPort, stationId);
        var c1Item = delivered.FirstOrDefault(x => x.GetProperty("commandId").GetInt64() == c1);
        var signature = c1Item.ValueKind == JsonValueKind.Object ? c1Item.GetProperty("signature").GetString() : null;
        var remote = new RemoteCommand
        {
            CommandId = c1,
            StationId = stationId,
            Type = CommandType.RunSelfCheck,
            PayloadJson = null,
            IssuedAt = c1Item.ValueKind == JsonValueKind.Object ? c1Item.GetProperty("issuedAt").GetDateTime() : DateTime.Now,
            TimeoutSeconds = 300,
            Signature = signature ?? string.Empty
        };
        Pass("有效签名实时验签下发", delivered.Any(x => x.GetProperty("commandId").GetInt64() == c1) &&
                                  signature is not null && signature != "unsigned" &&
                                  Sm2LicenseSigner.Verify(publicPem, RemoteCommandSignature.Canonical(remote), signature!),
            $"下发 {delivered.Count} 条，签名可验={signature is not null && signature != "unsigned"}");
    }
    catch (Exception ex)
    {
        Pass("有效签名实时验签下发", false, ex.Message);
    }

    // ---------- 2. 篡改载荷（Type 被改）：读取时验签失败，拒绝下发并标记 Failed ----------
    try
    {
        var c2 = await Dispatch(admin, KeyedPort, stationId, CommandType.ClearCache);
        using (var tamper = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            tamper.Ado.ExecuteCommand("update platform_command set Type=@t where Id=@id", new { t = (int)CommandType.StopCollecting, id = c2 });
        }

        var delivered = await Poll(admin, KeyedPort, stationId);
        using (var check = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            var row = check.Queryable<PlatformCommand>().First(c => c.Id == c2);
            Pass("篡改载荷拒绝下发", !delivered.Any(x => x.GetProperty("commandId").GetInt64() == c2) &&
                                     row.Status == CommandStatus.Failed &&
                                     row.ResultMessage!.Contains("签名校验失败"),
                $"未下发={!delivered.Any(x => x.GetProperty("commandId").GetInt64() == c2)}, 状态={row.Status}, 消息={row.ResultMessage}");
        }
    }
    catch (Exception ex)
    {
        Pass("篡改载荷拒绝下发", false, ex.Message);
    }

    // ---------- 3. 未签名指令（Signature='unsigned'）：拒绝下发并标记 Failed ----------
    try
    {
        var c3 = await Dispatch(admin, KeyedPort, stationId, CommandType.StopCollecting);
        using (var tamper = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            tamper.Ado.ExecuteCommand("update platform_command set Signature='unsigned' where Id=@id", new { id = c3 });
        }

        var delivered = await Poll(admin, KeyedPort, stationId);
        using (var check = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            var row = check.Queryable<PlatformCommand>().First(c => c.Id == c3);
            Pass("未签名指令拒绝下发", !delivered.Any(x => x.GetProperty("commandId").GetInt64() == c3) &&
                                      row.Status == CommandStatus.Failed &&
                                      row.ResultMessage!.Contains("未签名指令"),
                $"未下发={!delivered.Any(x => x.GetProperty("commandId").GetInt64() == c3)}, 消息={row.ResultMessage}");
        }
    }
    catch (Exception ex)
    {
        Pass("未签名指令拒绝下发", false, ex.Message);
    }
}
finally
{
    if (keyedPlatform is not null && !keyedPlatform.HasExited)
    {
        keyedPlatform.Kill();
    }
}

// ================= Phase B：未配置密钥（开发模式）兼容 =================
const int DevPort = 5115;
var devPlatform = StartPlatform(DevPort, null, platformMysql);
try
{
    using var devAdmin = await LoginAdmin(DevPort);
    var c4 = await Dispatch(devAdmin, DevPort, stationId, CommandType.RestartService);
    var delivered = await Poll(devAdmin, DevPort, stationId);
    using (var check = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = platformMysql,
        DbType = DbType.MySql,
        IsAutoCloseConnection = true
    }))
    {
        var row = check.Queryable<PlatformCommand>().First(c => c.Id == c4);
        Pass("开发模式兼容下发", delivered.Any(x => x.GetProperty("commandId").GetInt64() == c4) &&
                                row.Signature == "unsigned" && row.Status == CommandStatus.Pulled,
            $"下发={delivered.Any(x => x.GetProperty("commandId").GetInt64() == c4)}, Signature={row.Signature}");
    }
}
catch (Exception ex)
{
    Pass("开发模式兼容下发", false, ex.Message);
}
finally
{
    if (devPlatform is not null && !devPlatform.HasExited)
    {
        devPlatform.Kill();
    }
}

using (var clean = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    clean.Ado.ExecuteCommand("delete from platform_command");
    clean.Ado.ExecuteCommand("delete from platform_station where StationCode='ST-CMD'");
}

try
{
    Directory.Delete(testRoot, true);
}
catch
{
    // 临时目录清理失败不影响结论
}

Console.WriteLine();
Console.WriteLine("================ M26 指令读取时实时验签 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record DispatchResponse(bool Success, int Code, string Message, DispatchData? Data);

internal sealed record DispatchData(long CommandId, int Status, string Message);

internal sealed record PollResponse(bool Success, int Code, string Message, List<JsonElement>? Data);
