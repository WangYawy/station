using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using MiniExcelLibs;
using MySqlConnector;
using PdfSharp.Pdf.IO;
using SqlSugar;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M47 Spike: 台账/报表导出 xlsx / PDF（CSV 回归 + 格式魔数 + 内容回读 + 权限）
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
const int PlatformPort = 5150;

var sampleDir = @"E:\Reny\station\archive\export-p1";
Directory.CreateDirectory(sampleDir);

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

// ---------- 0. 播种（站/部门/用户/文件/报警/记录仪/审计） ----------
long stationAId;
using (var seed = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    seed.Ado.ExecuteCommand("delete from platform_file_metadata where FileNo like 'ST-EX-%'");
    seed.Ado.ExecuteCommand("delete from platform_alert_report where StationId in (select Id from platform_station where StationCode='ST-EX')");
    seed.Ado.ExecuteCommand("delete from platform_recorder where RecorderSerial in ('R-EX-001','R-EX-002')");
    seed.Ado.ExecuteCommand("delete from station_audit_log where OperationType in ('login','config-apply')");
    seed.Ado.ExecuteCommand("delete from platform_station where StationCode='ST-EX'");
    seed.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
    seed.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
    seed.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
    seed.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");

    var id = new SnowflakeIdGenerator();
    var team1 = new Dept { Id = id.NextId(), Code = "TEAM1", Name = "一队", ParentId = 1, SortOrder = 1, IsActive = true };
    var grp1 = new Dept { Id = id.NextId(), Code = "GRP1", Name = "一组", ParentId = team1.Id, SortOrder = 1, IsActive = true };
    seed.Insertable(team1).ExecuteCommand();
    seed.Insertable(grp1).ExecuteCommand();

    var zhangSan = new User { Id = id.NextId(), UserNo = "zhangsan", Name = "张三", DeptId = grp1.Id, IsActive = true };
    var liSi = new User { Id = id.NextId(), UserNo = "lisi", Name = "李四", DeptId = team1.Id, IsActive = true };
    seed.Insertable(zhangSan).ExecuteCommand();
    seed.Insertable(liSi).ExecuteCommand();
    var operatorRole = seed.Queryable<Role>().Where(r => r.Code == "operator").First();
    seed.Insertable(new UserRole { Id = id.NextId(), UserId = zhangSan.Id, RoleId = operatorRole.Id }).ExecuteCommand();
    var hasher = new Sm3PasswordHasher();
    seed.Insertable(new Account { Id = id.NextId(), UserName = "zhangsan", PasswordHash = hasher.Hash("Test@123"), UserId = zhangSan.Id, IsEnabled = true }).ExecuteCommand();

    stationAId = id.NextId();
    seed.Insertable(new PlatformStation
    {
        Id = stationAId,
        StationCode = "ST-EX",
        CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac",
        OsVersion = "麒麟V10", CpuArch = "x86_64", SoftwareVersion = "0.1.0",
        DeptId = team1.Id, RegisteredAt = DateTime.Now, LastHeartbeatAt = DateTime.Now
    }).ExecuteCommand();

    var now = DateTime.Now;
    seed.Insertable(new PlatformFileMetadata
    {
        Id = id.NextId(), StationId = stationAId, LocalFileId = 1, FileNo = "ST-EX-1",
        FileName = "执法记录-上午执勤.mp4", Size = 262144000, Kind = FileKind.Video,
        Sm3 = "A".PadRight(64, 'A'), CollectedAt = now.AddMinutes(-30),
        RecorderSerial = "R-EX-001", UserNo = "zhangsan", DeptCode = "GRP1",
        StorageLocation = "ST-EX/2026-08-13/R-EX-001/zhangsan/GRP1/video", ReceivedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformFileMetadata
    {
        Id = id.NextId(), StationId = stationAId, LocalFileId = 2, FileNo = "ST-EX-2",
        FileName = "现场照片-车辆.jpg", Size = 4096000, Kind = FileKind.Image,
        Sm3 = "B".PadRight(64, 'B'), CollectedAt = now.AddMinutes(-10),
        RecorderSerial = "R-EX-002", UserNo = "lisi", DeptCode = "TEAM1",
        StorageLocation = "ST-EX/2026-08-13/R-EX-002/lisi/TEAM1/image", ReceivedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformAlertReport
    {
        Id = id.NextId(), StationId = stationAId, DeptId = team1.Id, LocalAlertId = 1,
        Type = AlertType.UsbFault, Level = AlertLevel.Warning, Status = AlertStatus.Pending,
        Source = "ST-EX", Message = "USB 设备接入异常", OccurredAt = now, ReceivedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformAlertReport
    {
        Id = id.NextId(), StationId = stationAId, DeptId = team1.Id, LocalAlertId = 2,
        Type = AlertType.NetworkDown, Level = AlertLevel.Critical, Status = AlertStatus.Pending,
        Source = "ST-EX", Message = "平台网络中断，本地缓存续传", OccurredAt = now, ReceivedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformRecorder
    {
        Id = id.NextId(), RecorderSerial = "R-EX-001", LastStationId = stationAId, DeptId = team1.Id,
        BoundUserNo = "zhangsan", BoundUserName = "张三", BoundDeptCode = "GRP1",
        IsWhitelisted = true, IsActive = true, FirstSeenAt = now, LastSeenAt = now, UpdatedAt = now,
        FileCount = 1, TotalSize = 262144000, LastFileAt = now.AddMinutes(-30)
    }).ExecuteCommand();
    seed.Insertable(new PlatformRecorder
    {
        Id = id.NextId(), RecorderSerial = "R-EX-002", LastStationId = stationAId, DeptId = team1.Id,
        BoundUserNo = "lisi", BoundUserName = "李四", BoundDeptCode = "TEAM1",
        IsWhitelisted = false, IsActive = true, FirstSeenAt = now, LastSeenAt = now, UpdatedAt = now,
        FileCount = 1, TotalSize = 4096000, LastFileAt = now.AddMinutes(-10)
    }).ExecuteCommand();
    seed.Insertable(new AuditLog
    {
        Id = id.NextId(), OperatorAccount = "admin", OperatorName = "系统管理员",
        DeptId = team1.Id, OperationType = "login", Target = "admin",
        Detail = "登录成功（导出验证）", Result = 1, CreatedAt = now
    }).ExecuteCommand();
}

// ---------- 1. 启动平台 ----------
var platformExe = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api\bin\Release\net8.0\Station.Platform.Api.exe";
var platform = Process.Start(new ProcessStartInfo
{
    FileName = platformExe,
    Arguments = $"--urls http://127.0.0.1:{PlatformPort}",
    WorkingDirectory = Path.GetDirectoryName(platformExe)!,
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    Environment =
    {
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{PlatformPort}",
        ["ASPNETCORE_ENVIRONMENT"] = "Development",
        ["STATION__DB__PROVIDER"] = "MySql",
        ["STATION__DB__CONNECTIONSTRING"] = platformMysql
    }
});
var platformStdout = platform!.StandardOutput.ReadToEndAsync();
var platformStderr = platform!.StandardError.ReadToEndAsync();

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

    try
    {
        using var netstat = System.Diagnostics.Process.Start(new ProcessStartInfo("netstat", "-ano")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        });
        var netstatOutput = await netstat!.StandardOutput.ReadToEndAsync();
        var listeners = netstatOutput.Split('\n')
            .Where(l => l.Contains($":{PlatformPort}") && l.Contains("LISTENING"))
            .Select(l => l.Trim())
            .ToList();
        Console.WriteLine($"[DIAG] {PlatformPort} 监听行: {string.Join(" | ", listeners)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DIAG] netstat 失败: {ex.Message}");
    }

    using var admin = await LoginPlatformAsync("admin", "Admin@123");
    using var operatorClient = await LoginPlatformAsync("zhangsan", "Test@123");
    Console.WriteLine($"[DIAG] admin/operator 登录就绪");

    var endpoints = new (string Name, string Path, int MinRows)[]
    {
        ("文件台账", "/api/v1/exports/files", 2),
        ("报警台账", "/api/v1/exports/alerts", 2),
        ("采集站台账", "/api/v1/exports/stations", 1),
        ("记录仪台账", "/api/v1/exports/recorders", 2),
        ("审计日志", "/api/v1/exports/audit-logs", 1),
        ("采集趋势", "/api/v1/exports/stats-trend?days=14", 14),
        ("采集排行", "/api/v1/exports/stats-stations", 1)
    };

    // ---------- 2. 每端点 xlsx / pdf / csv 验证 ----------
    foreach (var (name, path, minRows) in endpoints)
    {
        try
        {
            var sep = path.Contains('?') ? '&' : '?';
            var xlsxResp = await admin.GetAsync($"{path}{sep}format=xlsx");
            var xlsxBytes = await xlsxResp.Content.ReadAsByteArrayAsync();
            var xlsxOk = VerifyXlsx(xlsxBytes, minRows);
            if ((int)xlsxResp.StatusCode >= 400)
            {
                Console.WriteLine($"[DIAG] {name} xlsx 错误体: {Encoding.UTF8.GetString(xlsxBytes, 0, Math.Min(400, xlsxBytes.Length))}");
            }

            var pdfResp = await admin.GetAsync($"{path}{sep}format=pdf");
            var pdfBytes = await pdfResp.Content.ReadAsByteArrayAsync();
            var pdfOk = VerifyPdf(pdfBytes);
            if ((int)pdfResp.StatusCode >= 400)
            {
                Console.WriteLine($"[DIAG] {name} pdf 错误体: {Encoding.UTF8.GetString(pdfBytes, 0, Math.Min(500, pdfBytes.Length))}");
            }

            var csvResp = await admin.GetAsync($"{path}{sep}format=csv");
            var csvBytes = await csvResp.Content.ReadAsByteArrayAsync();
            if ((int)csvResp.StatusCode >= 400)
            {
                Console.WriteLine($"[DIAG] {name} csv 错误体: {Encoding.UTF8.GetString(csvBytes, 0, Math.Min(500, csvBytes.Length))}");
            }
            var csvOk = csvBytes.Length > 3 &&
                        csvBytes[0] == 0xEF && csvBytes[1] == 0xBB && csvBytes[2] == 0xBF &&
                        csvBytes.Length > 50 &&
                        Encoding.UTF8.GetString(csvBytes, 3, Math.Min(200, csvBytes.Length - 3)).Contains(',');

            if (name is "文件台账" or "审计日志")
            {
                await File.WriteAllBytesAsync(Path.Combine(sampleDir, $"{name}-sample.pdf"), pdfBytes);
                await File.WriteAllBytesAsync(Path.Combine(sampleDir, $"{name}-sample.xlsx"), xlsxBytes);
            }

            Console.WriteLine($"[DIAG] {name} 后 MySQL Threads_connected={await ThreadsConnectedAsync(platformMysql)}");
            Pass($"{name} 三格式", xlsxOk && pdfOk && csvOk,
                $"状态码 xlsx={(int)xlsxResp.StatusCode}/pdf={(int)pdfResp.StatusCode}/csv={(int)csvResp.StatusCode}, " +
                $"xlsx={xlsxOk}({xlsxBytes.Length}B), pdf={pdfOk}({pdfBytes.Length}B), csv={csvOk}({csvBytes.Length}B)");
        }
        catch (Exception ex)
        {
            Pass($"{name} 三格式", false, ex.Message);
        }
    }

    // ---------- 3. 非法格式回退 CSV ----------
    try
    {
        var bytes = await (await admin.GetAsync("/api/v1/exports/files?format=doc")).Content.ReadAsByteArrayAsync();
        var ok = bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Pass("非法格式回退 CSV", ok, $"format=doc 返回 {bytes.Length}B（BOM 前缀={ok}）");
    }
    catch (Exception ex)
    {
        Pass("非法格式回退 CSV", false, ex.Message);
    }

    // ---------- 4. 权限：操作员无 audit:export → 403 ----------
    try
    {
        var resp = await operatorClient.GetAsync("/api/v1/exports/audit-logs?format=pdf");
        if ((int)resp.StatusCode >= 400)
        {
            var body = await resp.Content.ReadAsByteArrayAsync();
            Console.WriteLine($"[DIAG] 操作员审计错误体: {Encoding.UTF8.GetString(body, 0, Math.Min(600, body.Length))}");
        }
        Pass("审计导出权限拦截", resp.StatusCode == HttpStatusCode.Forbidden,
            $"操作员导出审计={resp.StatusCode}（应 403）");
    }
    catch (Exception ex)
    {
        Pass("审计导出权限拦截", false, ex.Message);
    }
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        platform.Kill();
    }

    var stdout = await platformStdout;
    var stderr = await platformStderr;
    Console.WriteLine("[DIAG] ---- 平台 stdout 尾部 ----");
    Console.WriteLine(string.Join('\n', stdout.Split('\n').TakeLast(15)));
    if (!string.IsNullOrWhiteSpace(stderr))
    {
        Console.WriteLine("[DIAG] ---- 平台 stderr 尾部 ----");
        Console.WriteLine(string.Join('\n', stderr.Split('\n').TakeLast(15)));
    }
}

Console.WriteLine();
Console.WriteLine("================ xlsx/PDF 导出验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

static bool VerifyXlsx(byte[] bytes, int minRows)
{
    if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
    {
        return false;
    }

    using var ms = new MemoryStream(bytes);
    var rows = ms.Query(useHeaderRow: true).ToList();
    return rows.Count >= minRows;
}

static bool VerifyPdf(byte[] bytes)
{
    if (bytes.Length < 5 || bytes[0] != 0x25)
    {
        return false;
    }

    var text = Encoding.Latin1.GetString(bytes);
    if (!text.StartsWith("%PDF-") || !text.Contains("%%EOF") || !text.Contains("/FontFile"))
    {
        return false;
    }

    using var ms = new MemoryStream(bytes);
    using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.ReadOnly);
    return doc.PageCount >= 1;
}

static async Task<HttpClient> LoginPlatformAsync(string user, string pass)
{
    var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
    var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
    var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
    login.EnsureSuccessStatusCode();
    return http;
}

static async Task<int> ThreadsConnectedAsync(string connectionString)
{
    using var conn = new MySqlConnection(connectionString);
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SHOW STATUS LIKE 'Threads_connected'";
    using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();
    return Convert.ToInt32(reader.GetValue(1));
}
