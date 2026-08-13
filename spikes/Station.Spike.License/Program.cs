using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Station.Application;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Licensing;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using ProtocolType = Station.Contracts.ProtocolType;

// ---------------------------------------------------------------------------
// M16 Spike: 授权与机器指纹（离线激活/硬件绑定/篡改/到期中断采集）
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
    _ => $"Data Source={Path.Combine(AppContext.BaseDirectory, "spike_license.db")}"
};

var testRoot = @"E:\Reny\station\archive\license-test";
var simDir = Path.Combine(testRoot, "sim");
var (licensePrivatePem, licensePublicPem) = Sm2LicenseSigner.CreateKeyPair();

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Station:Db:Provider"] = provider.ToString(),
        ["Station:Db:ConnectionString"] = connStr,
        ["Station:Collect:CacheDirectory"] = Path.Combine(testRoot, "cache"),
        ["Station:Collect:SimulatedSourceDirectory"] = simDir,
        ["Station:Collect:SimulatedFileCount"] = "5",
        ["Station:Collect:SimulatedChunkDelayMs"] = "500",
        ["Station:License:PublicKeyPem"] = licensePublicPem,
        ["Station:License:PrivateKeyPem"] = licensePrivatePem,
        ["Station:License:TrialDays"] = "30",
        ["Station:License:ProductCode"] = "STATION-DESKTOP-1"
    })
    .Build();

var services = new ServiceCollection();
services.AddStationDatabase(config);
services.AddStationApplication(config);
await using var sp = services.BuildServiceProvider();

sp.GetRequiredService<IDatabaseInitializer>().EnsureCreated(
    typeof(LicenseInfo), typeof(CollectTask), typeof(CollectFile));

var fingerprint = sp.GetRequiredService<IMachineFingerprintProvider>();
var license = sp.GetRequiredService<ILicenseService>();
var generator = sp.GetRequiredService<LicenseGenerator>();
var collect = sp.GetRequiredService<ICollectTaskService>();

// ---------- 0. SM2 签名/验签 ----------
try
{
    var signature = Sm2LicenseSigner.Sign(licensePrivatePem, "payload");
    var ok = Sm2LicenseSigner.Verify(licensePublicPem, "payload", signature);
    var bad = !Sm2LicenseSigner.Verify(licensePublicPem, "tampered", signature);
    Pass("SM2 签名/验签", ok && bad, $"验签={ok}, 篡改拒绝={bad}");
}
catch (Exception ex)
{
    Pass("SM2 签名/验签", false, ex.Message);
}

// ---------- 1. 机器指纹采集 ----------
try
{
    var fp = fingerprint.CollectFingerprint();
    Pass("机器指纹", fp.Length == 64 && !fp.All(c => c == '0'),
        $"指纹(SM3)={fp}");
}
catch (Exception ex)
{
    Pass("机器指纹", false, ex.Message);
}

// ---------- 2. 生成授权（内部工具）并激活 ----------
try
{
    var fp = fingerprint.CollectFingerprint();
    var file = generator.Generate("ST-TEST", fp, DateTime.Now.AddDays(30));
    var verified = LicenseFileCodec.Verify(file, licensePublicPem);
    var (ok, message) = await license.ActivateAsync(LicenseFileCodec.Serialize(file));
    var check = await license.CheckAsync();
    Pass("生成+激活", verified && ok && check.Status == LicenseStatus.Activated && check.DaysLeft >= 29,
        $"签名有效={verified}, 激活={message}, 状态={check.Status}, 剩余={check.DaysLeft}天");
}
catch (Exception ex)
{
    Pass("生成+激活", false, ex.Message);
}

// ---------- 3. 篡改签名拒绝 ----------
try
{
    var fp = fingerprint.CollectFingerprint();
    var file = generator.Generate("ST-TEST", fp, DateTime.Now.AddDays(30));
    var tampered = file with { Signature = file.Signature[..^4] + "0000" };
    var (ok, message) = await license.ActivateAsync(LicenseFileCodec.Serialize(tampered));
    Pass("篡改拒绝", !ok && message.Contains("签名无效"), $"结果={message}");
}
catch (Exception ex)
{
    Pass("篡改拒绝", false, ex.Message);
}

// ---------- 4. 换硬件（指纹不匹配）拒绝 ----------
try
{
    var file = generator.Generate("ST-TEST", new string('a', 64), DateTime.Now.AddDays(30));
    var (ok, message) = await license.ActivateAsync(LicenseFileCodec.Serialize(file));
    Pass("换机拒绝", !ok && message.Contains("不匹配"), $"结果={message}");
}
catch (Exception ex)
{
    Pass("换机拒绝", false, ex.Message);
}

// ---------- 5. 到期 → 拒绝采集 ----------
try
{
    var fp = fingerprint.CollectFingerprint();
    var file = generator.Generate("ST-TEST", fp, DateTime.Now.AddSeconds(1));
    var (ok, _) = await license.ActivateAsync(LicenseFileCodec.Serialize(file));
    await Task.Delay(1500);
    var check = await license.CheckAsync();
    var rejected = false;
    try
    {
        await collect.CreateTaskAsync(new CollectDeviceInfo("记录仪-LIC", "SIM-LIC", ProtocolType.Ums), isAuto: true);
    }
    catch (InvalidOperationException ex)
    {
        rejected = ex.Message.Contains("授权不可用");
    }

    Pass("到期拒绝采集", ok && check.Status == LicenseStatus.Locked && !check.IsValid && rejected,
        $"状态={check.Status}, 有效={check.IsValid}, 采集被拒={rejected}");
}
catch (Exception ex)
{
    Pass("到期拒绝采集", false, ex.Message);
}

// ---------- 6. 采集中授权到期立即中断 ----------
try
{
    var fp = fingerprint.CollectFingerprint();
    var file = generator.Generate("ST-TEST", fp, DateTime.Now.AddSeconds(4));
    var (ok, _) = await license.ActivateAsync(LicenseFileCodec.Serialize(file));

    var task = await collect.CreateTaskAsync(new CollectDeviceInfo("记录仪-LIC2", "SIM-LIC2", ProtocolType.Ums), isAuto: true);
    var deadline = DateTime.Now.AddSeconds(60);
    while (DateTime.Now < deadline)
    {
        var current = await collect.GetTaskAsync(task.TaskId);
        if (current is not null && current.Status is
            CollectTaskStatus.Completed or CollectTaskStatus.Interrupted
            or CollectTaskStatus.Failed or CollectTaskStatus.Canceled)
        {
            break;
        }

        await Task.Delay(200);
    }

    var done = await collect.GetTaskAsync(task.TaskId);
    var files = await collect.GetTaskFilesAsync(task.TaskId);
    var hasAbnormalOrCanceled = files.Any(f =>
        f.Status is CollectFileStatus.Abnormal or CollectFileStatus.Canceled);
    Pass("到期中断采集", ok && done is { Status: CollectTaskStatus.Interrupted } && hasAbnormalOrCanceled,
        $"任务状态={done?.Status}, 异常/取消文件={files.Count(f => f.Status is CollectFileStatus.Abnormal or CollectFileStatus.Canceled)}");
}
catch (Exception ex)
{
    Pass("到期中断采集", false, ex.Message);
}

// ---------- 7. 清理 ----------
try
{
    var db = sp.GetRequiredService<ISqlSugarClient>();
    db.DbMaintenance.DropTable<LicenseInfo>();
    db.DbMaintenance.DropTable<CollectFile>();
    db.DbMaintenance.DropTable<CollectTask>();
    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, true);
    }

    Pass("清理", true, "已删除测试表与目录");
}
catch (Exception ex)
{
    Pass("清理", false, ex.Message);
}

Console.WriteLine();
Console.WriteLine($"================ {provider} 授权验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;
