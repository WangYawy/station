using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Application.Licensing;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Licensing;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using Station.Infrastructure.Storage;

// ---------------------------------------------------------------------------
// M39 Spike: 国密 SM4（CBC/CTR 随机访问/密钥文件/凭据解密/授权信息加密存储）
// ---------------------------------------------------------------------------

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var key = new byte[16];
System.Security.Cryptography.RandomNumberGenerator.Fill(key);

// ---------- 1. SM4-CBC 往返 + 错误密钥拒绝 ----------
try
{
    var plain = "授权信息-测试-含中文-1234567890"u8.ToArray();
    var blob = Sm4Crypto.EncryptCbc(key, plain);
    var round = Sm4Crypto.DecryptCbc(key, blob);
    var wrongKey = new byte[16];
    System.Security.Cryptography.RandomNumberGenerator.Fill(wrongKey);
    var wrongOk = false;
    try
    {
        _ = Sm4Crypto.DecryptCbc(wrongKey, blob);
        wrongOk = true;
    }
    catch
    {
    }

    Pass("SM4-CBC", round.SequenceEqual(plain) && !wrongOk,
        $"往返一致={round.SequenceEqual(plain)}, 错误密钥拒绝={!wrongOk}");
}
catch (Exception ex)
{
    Pass("SM4-CBC", false, ex.ToString());
}

// ---------- 2. SM4-CTR 文件：整读 + 随机偏移（Range） ----------
try
{
    var root = Path.Combine(Path.GetTempPath(), "station-m39-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var plainPath = Path.Combine(root, "plain.bin");
    var encPath = Path.Combine(root, "enc.bin");
    var plain = new byte[1024 * 1024 + 37];
    System.Security.Cryptography.RandomNumberGenerator.Fill(plain);
    File.WriteAllBytes(plainPath, plain);

    Sm4Crypto.EncryptFile(plainPath, encPath, key);
    using (var reader = Sm4Crypto.CreateDecryptReader(encPath, key))
    {
        using var ms = new MemoryStream();
        reader.CopyTo(ms);
        var decrypted = ms.ToArray();
        var ok = decrypted.SequenceEqual(plain);
        if (!ok)
        {
            var firstDiff = Enumerable.Range(0, Math.Min(decrypted.Length, plain.Length))
                .FirstOrDefault(i => decrypted[i] != plain[i]);
            Console.WriteLine($"[DIAG] CTR首差异@{firstDiff}: 明文={plain[firstDiff]:X2} 解密={decrypted[firstDiff]:X2}, 解密长度={decrypted.Length}, 明文长度={plain.Length}");
        }

        Pass("SM4-CTR整读", ok, $"密文大小={new FileInfo(encPath).Length}");
    }

    var offsets = new[] { 0L, 1L, 15L, 16L, 1000L, 65536L, 1000000L };
    var sliceOk = true;
    foreach (var offset in offsets)
    {
        using var reader = Sm4Crypto.CreateDecryptReader(encPath, key, offset);
        var buf = new byte[300];
        var read = reader.Read(buf, 0, buf.Length);
        if (!plain.AsSpan((int)offset, read).SequenceEqual(buf.AsSpan(0, read)))
        {
            sliceOk = false;
            break;
        }
    }

    Pass("SM4-CTR随机偏移", sliceOk, $"偏移={string.Join(",", offsets)} 切片一致");
    try
    {
        Directory.Delete(root, true);
    }
    catch
    {
    }
}
catch (Exception ex)
{
    Pass("SM4-CTR", false, ex.ToString());
}

// ---------- 3. 密钥文件：创建、稳定性 ----------
try
{
    var keyRoot = Path.Combine(Path.GetTempPath(), "station-m39-keys-" + Guid.NewGuid().ToString("N"));
    var keyFile = Path.Combine(keyRoot, "sm4.key");
    var provider1 = new Sm4KeyProvider(keyFile);
    var provider2 = new Sm4KeyProvider(keyFile);
    var k1 = provider1.GetKey();
    var k2 = provider2.GetKey();
    Console.WriteLine($"[DIAG] keyFile存在={File.Exists(keyFile)}, k1={Convert.ToHexString(k1)}, k2={Convert.ToHexString(k2)}");
    Pass("SM4密钥文件", File.Exists(keyFile) && k1.SequenceEqual(k2) && k1.Length == 16,
        $"密钥文件已创建={File.Exists(keyFile)}, 两次读取一致={k1.SequenceEqual(k2)}");
    try
    {
        Directory.Delete(keyRoot, true);
    }
    catch
    {
    }
}
catch (Exception ex)
{
    Pass("SM4密钥文件", false, ex.ToString());
}

// ---------- 4. 凭据保护器 ----------
try
{
    var protectedValue = Sm4SecretProtector.Protect("Kingbase@123");
    var unprotected = Sm4SecretProtector.TryUnprotect(protectedValue);
    var passthrough = Sm4SecretProtector.TryUnprotect("plain-secret");
    Pass("凭据保护器", protectedValue.StartsWith("sm4:") && unprotected == "Kingbase@123" && passthrough == "plain-secret",
        $"前缀={protectedValue.StartsWith("sm4:")}");
}
catch (Exception ex)
{
    Pass("凭据保护器", false, ex.ToString());
}

// ---------- 5. sm4: 加密凭据连接真实 SFTP（SftpStorageTarget 解密接线） ----------
try
{
    var encryptedPassword = Sm4SecretProtector.Protect("Kingbase@123");
    var storage = new SftpStorageTarget(Options.Create(new StorageOptions
    {
        Target = StorageTargetKind.Sftp,
        SftpHost = "127.0.0.1",
        SftpPort = 2222,
        SftpUser = "kingbase",
        SftpPassword = encryptedPassword,
        ChunkBytes = 64 * 1024
    }));
    var local = Path.Combine(Path.GetTempPath(), $"station-m39-{Guid.NewGuid():N}.bin");
    File.WriteAllBytes(local, new byte[1024]);
    var remote = $"station-m39/{DateTime.Now:yyyyMMddHHmmss}/cred.bin";
    await storage.UploadAsync(new UploadTargetFile(local, remote, 1024), null, CancellationToken.None);
    var size = await storage.GetRemoteSizeAsync(remote, CancellationToken.None);
    using var cleanup = new Renci.SshNet.SftpClient("127.0.0.1", 2222, "kingbase", "Kingbase@123");
    cleanup.Connect();
    cleanup.DeleteFile(remote);
    cleanup.DeleteDirectory(remote[..remote.LastIndexOf('/')]);
    cleanup.DeleteDirectory("station-m39");
    cleanup.Disconnect();
    File.Delete(local);
    Pass("SM4加密凭据直连SFTP", size == 1024, $"上传成功, 远端大小={size}");
}
catch (Exception ex)
{
    Pass("SM4加密凭据直连SFTP", false, ex.ToString());
}

// ---------- 6. 授权信息 SM4 加密存储 ----------
try
{
    var dbFile = Path.Combine(Path.GetTempPath(), $"station-m39-{Guid.NewGuid():N}.db");
    using var db = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = $"Data Source={dbFile}",
        DbType = DbType.Sqlite,
        IsAutoCloseConnection = true
    });
    db.CodeFirst.InitTables(typeof(LicenseInfo), typeof(ClockState));
    var (privatePem, publicPem) = Sm2LicenseSigner.CreateKeyPair();
    var options = new LicenseOptions
    {
        PublicKeyPem = publicPem,
        PrivateKeyPem = privatePem,
        TrialDays = 30,
        ProductCode = "STATION-DESKTOP-1"
    };
    IMachineFingerprintProvider fingerprintProvider = new WindowsMachineFingerprintProvider();
    var fingerprint = fingerprintProvider.CollectFingerprint();
    var licenseText = new LicenseGenerator(options).GenerateFileText("ST001", fingerprint, DateTime.Now.AddDays(365));
    var service = new LicenseService(
        new RepositoryBase<LicenseInfo>(db),
        new RepositoryBase<ClockState>(db),
        options,
        fingerprintProvider,
        new SnowflakeIdGenerator());
    var activated = await service.ActivateAsync(licenseText);
    var stored = await db.Queryable<LicenseInfo>().FirstAsync();
    var decrypted = Sm4SecretProtector.TryUnprotect(stored.PayloadEnc);
    var check = await service.CheckAsync();
    Pass("授权信息加密存储", activated.Ok &&
                           stored.PayloadEnc!.StartsWith("sm4:") &&
                           !stored.PayloadEnc.Contains("LIC-") &&
                           decrypted == licenseText &&
                           check.Status == Station.Contracts.LicenseStatus.Activated,
        $"激活={activated.Ok}, PayloadEnc前缀={stored.PayloadEnc?.StartsWith("sm4:")}, 解密一致={decrypted == licenseText}, 状态={check.Status}");
    try
    {
        File.Delete(dbFile);
    }
    catch
    {
    }
}
catch (Exception ex)
{
    Pass("授权信息加密存储", false, ex.ToString());
}

Console.WriteLine();
Console.WriteLine("================ M39 国密 SM4 加密 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
