using Microsoft.Extensions.DependencyInjection;
using Station.Infrastructure.Licensing;
using Station.Infrastructure.Security;

// ---------------------------------------------------------------------------
// M34 Spike: 机器指纹（Windows WMI + Linux sysfs 模拟根 + DI 按 OS 选择）
// ---------------------------------------------------------------------------

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

// ---------- 1. Windows 真实现（当前运行环境） ----------
try
{
    IMachineFingerprintProvider windows = new WindowsMachineFingerprintProvider();
    var parts = windows.CollectParts();
    var fp1 = windows.CollectFingerprint();
    var fp2 = windows.CollectFingerprint();
    Pass("Windows指纹", !string.IsNullOrEmpty(parts.CpuSerial) &&
                        !string.IsNullOrEmpty(parts.MotherboardSerial) &&
                        !string.IsNullOrEmpty(parts.DiskSerial) &&
                        !string.IsNullOrEmpty(parts.MacAddress) &&
                        fp1 == fp2 &&
                        fp1 == Sm3Checksum.ComputeString(parts.ToRaw()),
        $"cpu={parts.CpuSerial}, mb={parts.MotherboardSerial}, disk={parts.DiskSerial}, mac={parts.MacAddress}");
}
catch (Exception ex)
{
    Pass("Windows指纹", false, ex.ToString());
}

// ---------- 2. Linux sysfs 模拟根 ----------
try
{
    var root = Path.Combine(Path.GetTempPath(), "station-m34-sysfs-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "class", "dmi", "id"));
    Directory.CreateDirectory(Path.Combine(root, "block", "sda", "device"));
    Directory.CreateDirectory(Path.Combine(root, "block", "loop0"));
    Directory.CreateDirectory(Path.Combine(root, "block", "ram0", "device"));
    File.WriteAllText(Path.Combine(root, "class", "dmi", "id", "product_uuid"), "UUID-123\n");
    File.WriteAllText(Path.Combine(root, "class", "dmi", "id", "board_serial"), "BOARD-456\n");
    File.WriteAllText(Path.Combine(root, "class", "dmi", "id", "product_name"), "PHY\n");
    File.WriteAllText(Path.Combine(root, "block", "sda", "device", "serial"), "DISK-789\n");
    File.WriteAllText(Path.Combine(root, "block", "loop0", "serial"), "LOOP\n");
    File.WriteAllText(Path.Combine(root, "block", "ram0", "device", "serial"), "RAM\n");

    IMachineFingerprintProvider linux = new LinuxMachineFingerprintProvider(root);
    var parts = linux.CollectParts();
    var fp1 = linux.CollectFingerprint();
    var fp2 = ((IMachineFingerprintProvider)new LinuxMachineFingerprintProvider(root)).CollectFingerprint();
    Pass("Linux指纹", parts.CpuSerial == "UUID-123" &&
                       parts.MotherboardSerial == "BOARD-456" &&
                       parts.DiskSerial == "DISK-789" &&
                       fp1 == fp2 &&
                       fp1 == Sm3Checksum.ComputeString(parts.ToRaw()),
        $"cpu={parts.CpuSerial}, mb={parts.MotherboardSerial}, disk={parts.DiskSerial}, mac={parts.MacAddress}");
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
    Pass("Linux指纹", false, ex.ToString());
}

// ---------- 3. 缺失/无权限回退 ----------
try
{
    var emptyRoot = Path.Combine(Path.GetTempPath(), "station-m34-empty-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(emptyRoot);
    var parts = new LinuxMachineFingerprintProvider(emptyRoot).CollectParts();
    Pass("Linux缺失回退", parts.CpuSerial == "unknown" &&
                          parts.MotherboardSerial == "unknown" &&
                          parts.DiskSerial == "unknown",
        $"cpu={parts.CpuSerial}, mb={parts.MotherboardSerial}, disk={parts.DiskSerial}");
    try
    {
        Directory.Delete(emptyRoot, true);
    }
    catch
    {
    }
}
catch (Exception ex)
{
    Pass("Linux缺失回退", false, ex.ToString());
}

// ---------- 4. DI 按 OS 选择 ----------
try
{
    var services = new ServiceCollection();
    services.AddSingleton<IMachineFingerprintProvider>(_ =>
        OperatingSystem.IsWindows()
            ? new WindowsMachineFingerprintProvider()
            : new LinuxMachineFingerprintProvider());
    await using var sp = services.BuildServiceProvider();
    var resolved = sp.GetRequiredService<IMachineFingerprintProvider>();
    Pass("DI选择指纹提供者", OperatingSystem.IsWindows()
        ? resolved is WindowsMachineFingerprintProvider
        : resolved is LinuxMachineFingerprintProvider,
        $"resolved={resolved.GetType().Name}");
}
catch (Exception ex)
{
    Pass("DI选择指纹提供者", false, ex.ToString());
}

Console.WriteLine();
Console.WriteLine("================ M34 机器指纹跨平台 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
