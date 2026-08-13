namespace Station.Infrastructure.Licensing;

/// <summary>机器指纹采集：CPU + 主板 + 磁盘 + MAC（换硬件即新指纹）。</summary>
public interface IMachineFingerprintProvider
{
    string CollectFingerprint();
}
