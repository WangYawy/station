using Microsoft.Extensions.Options;
using Renci.SshNet;
using Station.Infrastructure.Storage;

// ---------------------------------------------------------------------------
// M33 Spike: SFTP 真实服务器联调（SSH.NET ↔ WSL OpenSSH 2222）
//  - 认证连通性（密码/键盘交互）
//  - SftpStorageTarget 端到端：上传 → 远端大小校验 → 追加续传 → 清理
// ---------------------------------------------------------------------------

var host = "127.0.0.1";
var port = 2222;
var user = "kingbase";
var password = "Kingbase@123";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

// ---------- 1. 认证连通性 ----------
void Try(string name, ConnectionInfo info)
{
    try
    {
        using var client = new SftpClient(info);
        client.Connect();
        var entries = client.ListDirectory(".").Take(3).Select(e => e.Name).ToList();
        var home = client.WorkingDirectory;
        Console.WriteLine($"[PASS] {name}: 已连接, 当前目录={client.WorkingDirectory}, 条目={string.Join(",", entries)}");
        client.Disconnect();
        Pass(name, true, $"home={home}");
    }
    catch (Exception ex)
    {
        Pass(name, false, ex.Message);
    }
}

var passwordAuth = new PasswordAuthenticationMethod(user, password);
var keyboardAuth = new KeyboardInteractiveAuthenticationMethod(user);
keyboardAuth.AuthenticationPrompt += (_, e) =>
{
    foreach (var prompt in e.Prompts)
    {
        prompt.Response = password;
    }
};

Try("SSH.NET密码认证", new ConnectionInfo(host, port, user, passwordAuth));
Try("SSH.NET键盘交互认证", new ConnectionInfo(host, port, user, keyboardAuth));

// ---------- 2. SftpStorageTarget 端到端 ----------
var runDir = $"station-m33/{DateTime.Now:yyyyMMddHHmmss}";
var remotePath = $"{runDir}/record.bin";
var localFile = Path.Combine(Path.GetTempPath(), $"station-m33-{Guid.NewGuid():N}.bin");
var chunk = 1024 * 1024;

try
{
    var storage = new SftpStorageTarget(Options.Create(new StorageOptions
    {
        Target = StorageTargetKind.Sftp,
        SftpHost = host,
        SftpPort = port,
        SftpUser = user,
        SftpPassword = password,
        ChunkBytes = chunk
    }));

    // 上传 1MB（分块流式，FileMode.Append）
    File.WriteAllBytes(localFile, new byte[chunk]);
    await storage.UploadAsync(new UploadTargetFile(localFile, remotePath, chunk), null, CancellationToken.None);
    var size1 = await storage.GetRemoteSizeAsync(remotePath, CancellationToken.None);
    Pass("SFTP上传+远端大小校验", size1 == chunk, $"远端={size1} 期望={chunk}");

    // 追加 512KB（模拟断点续传：同一远端路径 FileMode.Append）
    var localAppend = Path.Combine(Path.GetTempPath(), $"station-m33-{Guid.NewGuid():N}.bin");
    File.WriteAllBytes(localAppend, new byte[chunk / 2]);
    await storage.UploadAsync(new UploadTargetFile(localAppend, remotePath, chunk / 2), null, CancellationToken.None);
    var size2 = await storage.GetRemoteSizeAsync(remotePath, CancellationToken.None);
    Pass("SFTP追加续传", size2 == chunk + chunk / 2, $"追加后远端={size2} 期望={chunk + chunk / 2}");

    // 清理远端文件与目录
    using var cleanup = new SftpClient(host, port, user, password);
    cleanup.Connect();
    cleanup.DeleteFile(remotePath);
    cleanup.DeleteDirectory(runDir);
    cleanup.DeleteDirectory("station-m33");
    cleanup.Disconnect();
    Pass("SFTP远端清理", true, $"已删除 {remotePath}");
}
catch (Exception ex)
{
    Pass("SFTP端到端", false, ex.ToString());
}
finally
{
    try
    {
        File.Delete(localFile);
    }
    catch
    {
    }
}

Console.WriteLine();
Console.WriteLine("================ M33 SFTP 真实服务器联调 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
