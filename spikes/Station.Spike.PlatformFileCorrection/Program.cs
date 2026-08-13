using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using SqlSugar;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M45 Spike: 文件归属修正（留痕 + SM2 签名验签）
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var (privatePem, publicPem) = Sm2LicenseSigner.CreateKeyPair();
var keyFile = Path.Combine(Path.GetTempPath(), $"station-m45-{Guid.NewGuid():N}.pem");
File.WriteAllText(keyFile, privatePem);

const int PlatformPort = 5131;
long stationAId;
var platformExe = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api\bin\Release\net8.0\Station.Platform.Api.exe";
var platform = Process.Start(new ProcessStartInfo
{
    FileName = platformExe,
    Arguments = $"--urls http://127.0.0.1:{PlatformPort} --Platform:Command:PrivateKeyPemFile={keyFile}",
    WorkingDirectory = Path.GetDirectoryName(platformExe)!,
    UseShellExecute = false,
    CreateNoWindow = true,
    Environment =
    {
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{PlatformPort}",
        ["STATION__DB__PROVIDER"] = "MySql",
        ["STATION__DB__CONNECTIONSTRING"] = platformMysql
    }
});

try
{
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

    using (var seed = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = platformMysql,
        DbType = DbType.MySql,
        IsAutoCloseConnection = true
    }))
    {
        seed.Ado.ExecuteCommand("delete from platform_file_correction");
        seed.Ado.ExecuteCommand("delete from platform_file_metadata");
        seed.Ado.ExecuteCommand("delete from platform_station where StationCode in ('ST-A')");
        seed.Ado.ExecuteCommand("delete from station_audit_log where OperationType in ('file.correct','login')");
        seed.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
        seed.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
        seed.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
        seed.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");

        var id = new SnowflakeIdGenerator();
        var team1 = new Dept { Id = id.NextId(), Code = "TEAM1", Name = "一队", ParentId = 1, SortOrder = 1 };
        var grp1 = new Dept { Id = id.NextId(), Code = "GRP1", Name = "一组", ParentId = team1.Id, SortOrder = 1 };
        seed.Insertable(team1).ExecuteCommand();
        seed.Insertable(grp1).ExecuteCommand();
        var zhangSan = new User { Id = id.NextId(), UserNo = "zhangsan", Name = "张三", DeptId = grp1.Id };
        var liSi = new User { Id = id.NextId(), UserNo = "lisi", Name = "李四", DeptId = team1.Id };
        seed.Insertable(zhangSan).ExecuteCommand();
        seed.Insertable(liSi).ExecuteCommand();
        var operatorRole = seed.Queryable<Role>().Where(r => r.Code == "operator").First();
        var managerRole = seed.Queryable<Role>().Where(r => r.Code == "manager").First();
        seed.Insertable(new UserRole { Id = id.NextId(), UserId = zhangSan.Id, RoleId = operatorRole.Id }).ExecuteCommand();
        seed.Insertable(new UserRole { Id = id.NextId(), UserId = liSi.Id, RoleId = managerRole.Id }).ExecuteCommand();
        var hasher = new Sm3PasswordHasher();
        seed.Insertable(new Account { Id = id.NextId(), UserName = "zhangsan", PasswordHash = hasher.Hash("Test@123"), UserId = zhangSan.Id }).ExecuteCommand();
        seed.Insertable(new Account { Id = id.NextId(), UserName = "lisi", PasswordHash = hasher.Hash("Test@123"), UserId = liSi.Id }).ExecuteCommand();

        stationAId = id.NextId();
        seed.Insertable(new PlatformStation
        {
            Id = stationAId,
            StationCode = "ST-A",
            CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac",
            OsVersion = "win11", CpuArch = "x86_64", SoftwareVersion = "0.1.0",
            DeptId = team1.Id, RegisteredAt = DateTime.Now
        }).ExecuteCommand();
    }

    // 上报文件（归属 u_old/OLD）
    using (var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") })
    {
        await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", new
        {
            stationId = stationAId,
            localFileId = 1L,
            fileNo = "ST-A-1",
            fileName = "corr.mp4",
            size = 1024,
            kind = 0,
            sm3 = "s1",
            collectedAt = DateTime.Now,
            recorderSerial = "R-CORR",
            userNo = "u_old",
            deptCode = "OLD"
        });
    }

    static async Task<HttpClient> Login(int port, string user, string pass)
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
        login.EnsureSuccessStatusCode();
        return http;
    }

    using var admin = await Login(PlatformPort, "admin", "Admin@123");

    // ---------- 1. 归属修正 ----------
    try
    {
        var resp = await admin.PutAsJsonAsync("/api/v1/files/ST-A-1/ownership", new { userNo = "zhangsan", deptCode = "GRP1" });
        var files = await (await admin.GetAsync("/api/v1/files?page=1&size=10&keyword=ST-A-1")).Content.ReadFromJsonAsync<FilesPage>();
        var file = files!.Data!.Items!.First(x => x.FileNo == "ST-A-1");
        Pass("归属修正", resp.StatusCode == HttpStatusCode.OK &&
                        file.UserNo == "zhangsan" && file.DeptCode == "GRP1",
            $"HTTP {(int)resp.StatusCode}, user={file.UserNo}, dept={file.DeptCode}");
    }
    catch (Exception ex)
    {
        Pass("归属修正", false, ex.ToString());
    }

    // ---------- 2. 修正留痕 + SM2 验签 ----------
    try
    {
        var corrections = await (await admin.GetAsync("/api/v1/files/ST-A-1/corrections")).Content.ReadFromJsonAsync<CorrectionsResponse>();
        var row = corrections!.Data!.First();
        var canonical = $"{row.FileNo}|{row.OldUserNo}|{row.NewUserNo}|{row.OldDeptCode}|{row.NewDeptCode}|" +
                        $"{row.OperatorAccount}|{row.CorrectedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ss}";
        var verified = Sm2LicenseSigner.Verify(publicPem, canonical, row.Signature);
        Pass("修正留痕SM2验签", row.Signature != "unsigned" && verified,
            $"签名前8={row.Signature[..Math.Min(8, row.Signature.Length)]}, 验签={verified}");
    }
    catch (Exception ex)
    {
        Pass("修正留痕SM2验签", false, ex.ToString());
    }

    // ---------- 3. 操作员修正范围外文件 -> 404（有 file:manage，但文件不在其数据范围） ----------
    try
    {
        using var op = await Login(PlatformPort, "zhangsan", "Test@123");
        var denied = await op.PutAsJsonAsync("/api/v1/files/ST-A-1/ownership", new { userNo = "lisi" });
        Pass("操作员范围外404", denied.StatusCode == HttpStatusCode.NotFound, $"HTTP {(int)denied.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("操作员403", false, ex.ToString());
    }
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        try
        {
            platform.Kill();
            platform.WaitForExit(5000);
        }
        catch
        {
        }
    }

    try
    {
        File.Delete(keyFile);
    }
    catch
    {
    }
}

using (var clean = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    clean.Ado.ExecuteCommand("delete from platform_file_correction");
    clean.Ado.ExecuteCommand("delete from platform_file_metadata");
    clean.Ado.ExecuteCommand("delete from platform_station where StationCode in ('ST-A')");
    clean.Ado.ExecuteCommand("delete from station_audit_log where OperationType in ('file.correct','login')");
    clean.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
    clean.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");
}

Console.WriteLine();
Console.WriteLine("================ M45 文件归属修正（SM2 签名） ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record FilesPage(bool Success, int Code, string Message, FilesData? Data);

internal sealed record FilesData(int PageIndex, int PageSize, long TotalCount, List<FileItem>? Items);

internal sealed record FileItem(string FileNo, string FileName, string? UserNo, string? DeptCode);

internal sealed record CorrectionsResponse(bool Success, int Code, string Message, List<CorrectionItem>? Data);

internal sealed record CorrectionItem(long Id, string FileNo, string? OldUserNo, string? NewUserNo, string? OldDeptCode, string? NewDeptCode, string? OperatorAccount, DateTime CorrectedAt, string Signature);
