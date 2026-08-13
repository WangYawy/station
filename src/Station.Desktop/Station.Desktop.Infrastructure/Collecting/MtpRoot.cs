namespace Station.Desktop.Infrastructure.Collecting;

/// <summary>MTP 虚拟根目录约定（无文件系统盘符，形如 MTP://{PnP设备ID}）。</summary>
internal static class MtpRoot
{
    public const string RootScheme = "MTP://";

    public static bool IsMtpRoot(string recorderRoot) =>
        recorderRoot.StartsWith(RootScheme, StringComparison.OrdinalIgnoreCase);
}
