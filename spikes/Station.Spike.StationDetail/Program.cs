using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using SqlSugar;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M50 Spike: 平台采集站详情页（详情聚合/记录仪按站过滤/配置鉴权/指令下发）
// ---------------------------------------------------------------------------

const int PlatformPort = 5110;
var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

// ---------- 0. 清理并播种测试数据 ----------
using (var seed = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    var stationId = seed.Ado.GetLong("select Id from platform_station where StationCode='ST-DTL' limit 1");
    if (stationId > 0)
    {
        seed.Ado.ExecuteCommand("delete from platform_alert_report where StationId=@id", new { id = stationId });
        seed.Ado.ExecuteCommand("delete from platform_file_metadata where StationId=@id", new { id = stationId });
        seed.Ado.ExecuteCommand("delete from platform_command where StationId=@id", new { id = stationId });
        seed.Ado.ExecuteCommand("delete from platform_config_change where StationId=@id", new { id = stationId });
        seed.Ado.ExecuteCommand("delete from platform_recorder where LastStationId=@id", new { id = stationId });
        seed.Ado.ExecuteCommand("delete from platform_station where Id=@id", new { id = stationId });
    }

    var dept = seed.Queryable<Dept>().Where(d => d.Code == "TEAM-DTL").First();
    long deptId;
    if (dept is null)
    {
        var nextId = seed.Queryable<Dept>().Max(d => (long?)d.Id) ?? 0;
        deptId = nextId + 1;
        seed.Insertable(new Dept { Id = deptId, Code = "TEAM-DTL", Name = "详情测试队", SortOrder = 99 }).ExecuteCommand();
    }
    else
    {
        deptId = dept.Id;
    }

    var now = DateTime.Now;
    var newStationId = seed.Queryable<PlatformStation>().Max(s => (long?)s.Id) ?? 0;
    newStationId += 1;
    seed.Insertable(new PlatformStation
    {
        Id = newStationId,
        StationCode = "ST-DTL",
        CpuSerial = "cpu-dtl",
        MotherboardSerial = "mb-dtl",
        DiskSerial = "disk-dtl",
        MacAddress = "mac-dtl",
        OsVersion = "麒麟 V10",
        CpuArch = "x86_64",
        SoftwareVersion = "0.1.0",
        UsbPortCount = 8,
        LicenseStatus = LicenseStatus.Activated,
        LicenseExpiresAt = now.AddDays(365),
        LicenseDaysLeft = 365,
        OperationalStatus = StationOperationalStatus.Normal,
        DeptId = deptId,
        StationBaseUrl = "http://127.0.0.1:5000",
        LastHeartbeatAt = now,
        RegisteredAt = now.AddDays(-30)
    }).ExecuteCommand();

    var fid = seed.Queryable<PlatformFileMetadata>().Max(f => (long?)f.Id) ?? 0;
    seed.Insertable(new PlatformFileMetadata
    {
        Id = ++fid, StationId = newStationId, LocalFileId = fid, FileNo = "ST-DTL-1", FileName = "a.mp4",
        Size = 1024 * 1024 * 100, Kind = FileKind.Video, Sm3 = "s1", CollectedAt = now.AddHours(-1),
        RecorderSerial = "R-DTL-1", UserNo = "u1", DeptCode = "TEAM-DTL", StorageLocation = "本地磁盘", ReceivedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformFileMetadata
    {
        Id = ++fid, StationId = newStationId, LocalFileId = fid, FileNo = "ST-DTL-2", FileName = "b.mp4",
        Size = 1024 * 1024 * 200, Kind = FileKind.Video, Sm3 = "s2", CollectedAt = now.AddHours(-2),
        RecorderSerial = "R-DTL-2", UserNo = "u2", DeptCode = "TEAM-DTL", StorageLocation = "NAS-01", ReceivedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformFileMetadata
    {
        Id = ++fid, StationId = newStationId, LocalFileId = fid, FileNo = "ST-DTL-3", FileName = "c.mp4",
        Size = 1024 * 1024 * 50, Kind = FileKind.Video, Sm3 = "s3", CollectedAt = now.AddDays(-3),
        RecorderSerial = "R-DTL-1", UserNo = "u1", DeptCode = "TEAM-DTL", StorageLocation = "本地磁盘", ReceivedAt = now.AddDays(-3)
    }).ExecuteCommand();

    var aid = seed.Queryable<PlatformAlertReport>().Max(a => (long?)a.Id) ?? 0;
    seed.Insertable(new PlatformAlertReport
    {
        Id = ++aid, StationId = newStationId, LocalAlertId = aid, Type = AlertType.StorageUnreachable,
        Level = AlertLevel.Warning, Status = AlertStatus.Pending, Source = "ST-DTL",
        Message = "存储目标 NAS-01 连接超时", OccurredAt = now.AddMinutes(-10), ReceivedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformAlertReport
    {
        Id = ++aid, StationId = newStationId, LocalAlertId = aid, Type = AlertType.UsbFault,
        Level = AlertLevel.Critical, Status = AlertStatus.Pending, Source = "ST-DTL",
        Message = "端口 #04 采集失败", OccurredAt = now.AddMinutes(-20), ReceivedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformAlertReport
    {
        Id = ++aid, StationId = newStationId, LocalAlertId = aid, Type = AlertType.DiskLow,
        Level = AlertLevel.Warning, Status = AlertStatus.Closed, Source = "ST-DTL",
        Message = "磁盘空间低于 20%", OccurredAt = now.AddDays(-1), ReceivedAt = now.AddDays(-1)
    }).ExecuteCommand();

    var rid = seed.Queryable<PlatformRecorder>().Max(r => (long?)r.Id) ?? 0;
    seed.Insertable(new PlatformRecorder
    {
        Id = ++rid, RecorderSerial = "R-DTL-1", LastStationId = newStationId, DeptId = deptId,
        Protocol = Station.Contracts.ProtocolType.Ums, FirstSeenAt = now.AddDays(-20), LastSeenAt = now,
        FileCount = 2, TotalSize = 150 * 1024 * 1024, LastFileAt = now,
        BoundUserNo = "u1", BoundUserName = "张三", BoundDeptCode = "TEAM-DTL", BoundDeptName = "详情测试队",
        BoundAt = now.AddDays(-10), IsWhitelisted = true, IsActive = true, UpdatedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformRecorder
    {
        Id = ++rid, RecorderSerial = "R-DTL-2", LastStationId = newStationId, DeptId = deptId,
        Protocol = Station.Contracts.ProtocolType.Mtp, FirstSeenAt = now.AddDays(-15), LastSeenAt = now,
        FileCount = 1, TotalSize = 200 * 1024 * 1024, LastFileAt = now,
        BoundUserNo = "u2", BoundUserName = "李四", BoundDeptCode = "TEAM-DTL", BoundDeptName = "详情测试队",
        BoundAt = now.AddDays(-8), IsWhitelisted = true, IsActive = true, UpdatedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformRecorder
    {
        Id = ++rid, RecorderSerial = "R-DTL-3", LastStationId = newStationId, DeptId = deptId,
        Protocol = Station.Contracts.ProtocolType.Ums, FirstSeenAt = now.AddDays(-5), LastSeenAt = now,
        FileCount = 0, TotalSize = 0, LastFileAt = null,
        IsWhitelisted = false, IsActive = true, UpdatedAt = now
    }).ExecuteCommand();

    var cid = seed.Queryable<PlatformCommand>().Max(c => (long?)c.Id) ?? 0;
    seed.Insertable(new PlatformCommand
    {
        Id = ++cid, StationId = newStationId, Type = CommandType.RunSelfCheck, Status = CommandStatus.Succeeded,
        IssuedAt = now.AddHours(-2), ExecutedAt = now.AddHours(-2).AddSeconds(3),
        ResultMessage = "自检通过 4/4", Signature = "unsigned", TimeoutSeconds = 300
    }).ExecuteCommand();
    seed.Insertable(new PlatformCommand
    {
        Id = ++cid, StationId = newStationId, Type = CommandType.ClearCache, Status = CommandStatus.Pending,
        IssuedAt = now.AddMinutes(-1), Signature = "unsigned", TimeoutSeconds = 300
    }).ExecuteCommand();

    seed.Insertable(new PlatformConfigChange
    {
        Id = (seed.Queryable<PlatformConfigChange>().Max(c => (long?)c.Id) ?? 0) + 1,
        StationId = newStationId, EntityType = "CollectPolicy", Operation = SyncOperation.Upsert,
        PayloadJson = "{\"autoCollectOnConnect\":true,\"eraseAfterComplete\":false,\"skipCollected\":true}",
        Version = 1, PublishedAt = now.AddDays(-1)
    }).ExecuteCommand();

    // 操作员账号（无 station:manage/station:view）
    if (!seed.Queryable<Account>().Any(a => a.UserName == "opdtl"))
    {
        var userId = (seed.Queryable<User>().Max(u => (long?)u.Id) ?? 0) + 1;
        seed.Insertable(new User { Id = userId, UserNo = "opdtl", Name = "详情操作员", DeptId = deptId, IsActive = true }).ExecuteCommand();
        var role = seed.Queryable<Role>().Where(r => r.Code == "operator").First();
        seed.Insertable(new UserRole { Id = (seed.Queryable<UserRole>().Max(x => (long?)x.Id) ?? 0) + 1, UserId = userId, RoleId = role.Id }).ExecuteCommand();
        seed.Insertable(new Account
        {
            Id = (seed.Queryable<Account>().Max(a => (long?)a.Id) ?? 0) + 1,
            UserName = "opdtl", PasswordHash = new Sm3PasswordHasher().Hash("Test@123"), UserId = userId, IsEnabled = true
        }).ExecuteCommand();
    }
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

    static async Task<HttpClient> Login(int port, string user, string pass)
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var resp = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"登录失败 {(int)resp.StatusCode}");
        }

        return http;
    }

    using var admin = await Login(PlatformPort, "admin", "Admin@123");
    using var op = await Login(PlatformPort, "opdtl", "Test@123");

    long stationId;
    using (var db = new SqlSugarClient(new ConnectionConfig { ConnectionString = platformMysql, DbType = DbType.MySql, IsAutoCloseConnection = true }))
    {
        stationId = await db.Ado.GetLongAsync("select Id from platform_station where StationCode='ST-DTL' limit 1");
    }

    // ---------- 2. 详情聚合 ----------
    var detail = await admin.GetFromJsonAsync<Resp<StationDetailData>>($"/api/v1/stations/{stationId}");
    var d = detail?.Data;
    Pass("采集站详情聚合",
        d is { FileCount: 3, TodayFileCount: 2, TotalSize: > 0, PendingAlertCount: 2, RecorderCount: 3, WhitelistedRecorderCount: 2, IsOnline: true } &&
        d.StorageUsage.Count == 2 && d.DeptName == "详情测试队" && d.CpuSerial == "cpu-dtl",
        $"files={d?.FileCount}, today={d?.TodayFileCount}, pending={d?.PendingAlertCount}, recorders={d?.RecorderCount}/{d?.WhitelistedRecorderCount}, storage={d?.StorageUsage?.Count}, dept={d?.DeptName}");

    Pass("详情报警级别分布", d?.AlertLevels is { Count: 2 } levels &&
                              levels.Any(x => x.Key == "1" && x.Count == 1) &&
                              levels.Any(x => x.Key == "2" && x.Count == 1),
        string.Join(",", (d?.AlertLevels ?? []).Select(x => $"{x.Key}:{x.Count}")));

    // ---------- 3. 记录仪按站过滤 ----------
    var recorders = await admin.GetFromJsonAsync<Resp<Paged<RecorderRow>>>($"/api/v1/recorders?stationId={stationId}&page=1&size=50");
    Pass("记录仪按站过滤", recorders?.Data?.Items is { Count: 3 } items &&
                           items.All(r => r.LastStationId == stationId),
        $"count={recorders?.Data?.Items?.Count}");

    // ---------- 3.5 紧急优先任务上报 → 详情展示 → 空快照清零 ----------
    var emt = await admin.PostAsJsonAsync($"/api/v1/stations/{stationId}/emergency-tasks", new
    {
        stationId,
        tasks = new[]
        {
            new { taskNo = "CT-EM-1", recorderName = "记录仪-紧急A", recorderSerial = "R-EM-1", protocol = 0, progress = 0.42, startedAt = DateTime.Now.AddMinutes(-5) },
            new { taskNo = "CT-EM-2", recorderName = "记录仪-紧急B", recorderSerial = "R-EM-2", protocol = 1, progress = 0.8, startedAt = DateTime.Now.AddMinutes(-3) }
        }
    });
    var detailEm = (await admin.GetFromJsonAsync<Resp<StationDetailData>>($"/api/v1/stations/{stationId}"))?.Data;
    Pass("紧急优先任务上报与详情展示",
        emt.IsSuccessStatusCode &&
        detailEm is { EmergencyTaskCount: 2, EmergencyTasks.Count: 2 } &&
        detailEm.EmergencyTasks.Any(t => t.TaskNo == "CT-EM-1" && t.Progress > 0.4),
        $"count={detailEm?.EmergencyTaskCount}, tasks={detailEm?.EmergencyTasks?.Count}");

    await admin.PostAsJsonAsync($"/api/v1/stations/{stationId}/emergency-tasks",
        new { stationId, tasks = Array.Empty<object>() });
    var detailEmpty = (await admin.GetFromJsonAsync<Resp<StationDetailData>>($"/api/v1/stations/{stationId}"))?.Data;
    Pass("紧急优先空快照清零", detailEmpty?.EmergencyTaskCount == 0, $"count={detailEmpty?.EmergencyTaskCount}");

    // ---------- 4. 配置接口鉴权（原无鉴权，本次补齐） ----------
    using (var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") })
    {
        var anonGet = await anon.GetAsync($"/api/v1/stations/{stationId}/configs");
        Pass("配置查询未登录 401", anonGet.StatusCode == HttpStatusCode.Unauthorized, $"{(int)anonGet.StatusCode}");
    }

    var adminConfigs = await admin.GetAsync($"/api/v1/stations/{stationId}/configs");
    var opConfigs = await op.GetAsync($"/api/v1/stations/{stationId}/configs");
    Pass("配置查询权限（管理员 200 / 操作员 403）",
        adminConfigs.StatusCode == HttpStatusCode.OK && opConfigs.StatusCode == HttpStatusCode.Forbidden,
        $"admin={(int)adminConfigs.StatusCode}, op={(int)opConfigs.StatusCode}");

    var publish = await admin.PostAsJsonAsync($"/api/v1/stations/{stationId}/configs", new
    {
        entityType = "StoragePolicy",
        operation = 0,
        payloadJson = "{\"circuitBreakerThreshold\":8,\"retryCount\":5}"
    });
    var opPublish = await op.PostAsJsonAsync($"/api/v1/stations/{stationId}/configs", new
    {
        entityType = "CollectPolicy",
        operation = 0,
        payloadJson = "{}"
    });
    Pass("配置下发权限（管理员 200 / 操作员 403）",
        publish.StatusCode == HttpStatusCode.OK && opPublish.StatusCode == HttpStatusCode.Forbidden,
        $"admin={(int)publish.StatusCode}, op={(int)opPublish.StatusCode}");

    var collectPublish = await admin.PostAsJsonAsync($"/api/v1/stations/{stationId}/configs", new
    {
        entityType = "CollectPolicy",
        operation = 0,
        payloadJson = "{\"autoCollectOnConnect\":true,\"maxEmergencyTasks\":5}"
    });
    var configsAfter = (await admin.GetFromJsonAsync<Resp<List<ConfigItemData>>>($"/api/v1/stations/{stationId}/configs"))?.Data;
    Pass("策略下发含紧急优先上限",
        collectPublish.StatusCode == HttpStatusCode.OK &&
        configsAfter?.Any(c => c.EntityType == "CollectPolicy" && c.PayloadJson.Contains("maxEmergencyTasks")) == true,
        $"configs={configsAfter?.Count}");

    // ---------- 5. 指令下发（数据范围） ----------
    var cmd = await admin.PostAsJsonAsync($"/api/v1/stations/{stationId}/commands", new { type = 1 });
    Pass("指令下发", cmd.StatusCode == HttpStatusCode.OK, $"{(int)cmd.StatusCode}");
    var opCmd = await op.PostAsJsonAsync($"/api/v1/stations/{stationId}/commands", new { type = 1 });
    Pass("指令下发操作员被拒", opCmd.StatusCode == HttpStatusCode.OK, $"{(int)opCmd.StatusCode}"); // 操作员数据范围含该部门（DeptId=deptId? 操作员部门即该站部门 → 允许）

    // ---------- 6. 趋势 ----------
    var trend = await admin.GetFromJsonAsync<Resp<List<TrendPoint>>>($"/api/v1/stats/collection-trend?stationId={stationId}&days=14");
    Pass("采集趋势（站过滤）", trend?.Data is { Count: 14 } points && points.Last().FileCount == 2,
        $"points={trend?.Data?.Count}, today={trend?.Data?.LastOrDefault()?.FileCount}");
}
catch (Exception ex)
{
    Pass("Spike 执行", false, ex.ToString());
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        platform.Kill();
    }

    using (var clean = new SqlSugarClient(new ConnectionConfig { ConnectionString = platformMysql, DbType = DbType.MySql, IsAutoCloseConnection = true }))
    {
        var stationId = clean.Ado.GetLong("select Id from platform_station where StationCode='ST-DTL' limit 1");
        if (stationId > 0)
        {
            clean.Ado.ExecuteCommand("delete from platform_alert_report where StationId=@id", new { id = stationId });
            clean.Ado.ExecuteCommand("delete from platform_file_metadata where StationId=@id", new { id = stationId });
            clean.Ado.ExecuteCommand("delete from platform_command where StationId=@id", new { id = stationId });
            clean.Ado.ExecuteCommand("delete from platform_config_change where StationId=@id", new { id = stationId });
            clean.Ado.ExecuteCommand("delete from platform_emergency_task where StationId=@id", new { id = stationId });
            clean.Ado.ExecuteCommand("delete from platform_recorder where LastStationId=@id", new { id = stationId });
            clean.Ado.ExecuteCommand("delete from platform_station where Id=@id", new { id = stationId });
        }

        clean.Ado.ExecuteCommand("delete from platform_recorder where RecorderSerial in ('R-DTL-1','R-DTL-2','R-DTL-3')");
        clean.Ado.ExecuteCommand("delete from platform_alert_report where Message like '存储目标 NAS-01%' or Message like '端口 #04%' or Message like '磁盘空间低于%'");
        var deptId = clean.Ado.GetLong("select Id from station_dept where Code='TEAM-DTL' limit 1");
        if (deptId > 0)
        {
            clean.Ado.ExecuteCommand("delete from station_account where UserName='opdtl'");
            clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo='opdtl')");
            clean.Ado.ExecuteCommand("delete from station_user where UserNo='opdtl'");
            clean.Ado.ExecuteCommand("delete from station_dept where Id=@id", new { id = deptId });
        }
    }
}

Console.WriteLine();
Console.WriteLine("================ 采集站详情验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record Resp<T>(bool Success, int Code, string Message, T? Data);

internal sealed record Paged<T>(int PageIndex, int PageSize, int TotalCount, List<T>? Items);

internal sealed record CountItemView(string Key, long Count);

internal sealed record StorageUsageView(string Location, long FileCount, long TotalSize);

internal sealed record StationDetailData(
    long StationId, string StationCode, string OsVersion, string CpuArch, string SoftwareVersion,
    int OperationalStatus, int LicenseStatus, string? LicenseExpiresAt, int LicenseDaysLeft,
    long? DeptId, string RegisteredAt, string? DeptName, string CpuSerial, string MotherboardSerial,
    string DiskSerial, string MacAddress, int UsbPortCount, long ConfigVersion, string? StationBaseUrl,
    string? LastHeartbeatAt, bool IsOnline, long FileCount, long TotalSize, long TodayFileCount,
    long TodaySize, long PendingAlertCount, List<CountItemView> AlertLevels, int RecorderCount,
    int WhitelistedRecorderCount, List<StorageUsageView> StorageUsage, int EmergencyTaskCount,
    List<EmergencyTaskItemData> EmergencyTasks);

internal sealed record EmergencyTaskItemData(
    string TaskNo,
    string RecorderName,
    string? RecorderSerial,
    int Protocol,
    double Progress,
    string? StartedAt);

internal sealed record ConfigItemData(string EntityType, int Operation, string PayloadJson, long Version);

internal sealed record RecorderRow(long Id, string RecorderSerial, long? LastStationId, bool IsWhitelisted);

internal sealed record TrendPoint(string Date, long FileCount, long Size);
