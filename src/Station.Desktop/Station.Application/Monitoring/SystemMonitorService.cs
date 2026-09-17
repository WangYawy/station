using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Station.Application.Collecting;

namespace Station.Application.Monitoring;

public sealed record MonitorLine(string Label, string Value);

/// <summary>
/// 本机状态监控：CPU/内存/磁盘/网络/监听端口/设备（Windows 性能计数器 + Linux /proc，跨平台降级 "—"）。
/// </summary>
public sealed class SystemMonitorService
{
    private readonly CollectOptions _collectOptions;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memCounter;
    private (long Idle, long Total)? _lastCpu;
    private long _lastRx;
    private long _lastTx;
    private DateTime _lastNet = DateTime.MinValue;

    public SystemMonitorService(CollectOptions collectOptions)
    {
        _collectOptions = collectOptions;
    }

    public IReadOnlyList<MonitorLine> Snapshot()
    {
        return
        [
            new("CPU", CpuPercent()),
            new("内存", MemoryText()),
            new("磁盘", DiskText()),
            //new("网络", NetworkText()),
            //new("监听端口", PortsText()),
            //new("设备", DeviceText())
        ];
    }

    private string CpuPercent()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _cpuCounter ??= new PerformanceCounter("Processor", "% Processor Time", "_Total");
                return $"{_cpuCounter.NextValue():F0}%";
            }

            var cpu = File.ReadLines("/proc/stat").FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cpu is not { Length: >= 6 } || !long.TryParse(cpu[1], out _))
            {
                return "—";
            }

            var idle = long.Parse(cpu[4]) + long.Parse(cpu[5]);
            var total = cpu.Skip(1).Sum(s => long.TryParse(s, out var v) ? v : 0);
            if (_lastCpu is { } last)
            {
                var dIdle = idle - last.Idle;
                var dTotal = total - last.Total;
                _lastCpu = (idle, total);
                return dTotal <= 0 ? "0%" : $"{100d * (1 - (double)dIdle / dTotal):F0}%";
            }

            _lastCpu = (idle, total);
            return "—";
        }
        catch
        {
            return "—";
        }
    }

    private string MemoryText()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _memCounter ??= new PerformanceCounter("Memory", "Available MBytes");
                var availableMb = _memCounter.NextValue();
                var totalBytes = 0L;
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    totalBytes = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                    break;
                }

                return totalBytes > 0
                    ? $"可用 {availableMb / 1024d:F1}GB / {totalBytes / 1024d / 1024 / 1024:F0}GB"
                    : $"可用 {availableMb / 1024d:F1}GB";
            }

            var mem = File.ReadLines("/proc/meminfo").Take(2).Select(l => l.Split(':', 2)).Where(kv => kv.Length == 2)
                .ToDictionary(kv => kv[0].Trim(), kv => kv[1].Trim().Split(' ')[0]);
            if (mem.TryGetValue("MemTotal", out var totalKb) && mem.TryGetValue("MemAvailable", out var availKb))
            {
                return $"可用 {long.Parse(availKb) / 1024d / 1024:F1}GB / {long.Parse(totalKb) / 1024d / 1024:F0}GB";
            }

            return "—";
        }
        catch
        {
            return "—";
        }
    }

    private static string DiskText()
    {
        try
        {
            long total = 0;
            long free = 0;
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                total += drive.TotalSize;
                free += drive.AvailableFreeSpace;
            }

            return total > 0
                ? $"已用 {(total - free) / 1024d / 1024 / 1024:F0}GB / {total / 1024d / 1024 / 1024:F0}GB"
                : "—";
        }
        catch
        {
            return "—";
        }
    }

    private string NetworkText()
    {
        try
        {
            long rx = 0;
            long tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                var stats = ni.GetIPv4Statistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }

            var now = DateTime.Now;
            if (_lastNet == DateTime.MinValue)
            {
                _lastRx = rx;
                _lastTx = tx;
                _lastNet = now;
                return "—";
            }

            var seconds = Math.Max(1, (now - _lastNet).TotalSeconds);
            var up = (rx - _lastRx) / seconds;
            var down = (tx - _lastTx) / seconds;
            _lastRx = rx;
            _lastTx = tx;
            _lastNet = now;
            return $"↑ {up / 1024:F0}KB/s ↓ {down / 1024:F0}KB/s";
        }
        catch
        {
            return "—";
        }
    }

    private static string PortsText()
    {
        try
        {
            var ports = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Select(e => e.Port)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            return ports.Count == 0 ? "—" : string.Join(",", ports.Take(8)) + (ports.Count > 8 ? "…" : "");
        }
        catch
        {
            return "—";
        }
    }

    private string DeviceText()
    {
        try
        {
            var removable = DriveInfo.GetDrives().Count(d => d.DriveType == DriveType.Removable);
            return _collectOptions.SourceMode == "ums"
                ? $"UMS 设备 {removable} 个"
                : $"模拟源（UMS {removable} 个）";
        }
        catch
        {
            return "—";
        }
    }
}
