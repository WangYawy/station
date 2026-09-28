using System.Runtime.Versioning;

namespace Station.Crypto.Engine.Keys;

/// <summary>
/// 主密钥文件读写工具（internal）。
///
/// 职责：
///   - 加载 / 自动创建主密钥文件
///   - 跨平台权限收紧（Windows ACL / Unix 600）
///
/// 不负责版本切换、派生、轮换策略（由 <see cref="FileMasterKeyProvider"/> 负责）。
/// </summary>
internal static class MasterKeyFileStore
{
    /// <summary>
    /// 加载主密钥文件；若不存在或损坏，则新建一个（含 1 个版本）。
    /// </summary>
    public static MasterKeyFileDto LoadOrCreate(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var dto = MasterKeyFileDto.FromJson(json);
                if (dto.Keys.Count > 0)
                    return dto;
                // 空文件或 keys 为空，视为损坏，走新建流程
            }
        }
        catch
        {
            // 读取失败（IO / 格式错误）→ 走新建流程
        }

        var initial = MasterKeyFileDto.Create(1);
        Save(filePath, initial);
        return initial;
    }

    /// <summary>同步保存。</summary>
    public static void Save(string filePath, MasterKeyFileDto dto)
    {
        EnsureDirectory(filePath);
        File.WriteAllText(filePath, dto.ToJson());
        TryRestrictPermissions(filePath);
    }

    /// <summary>异步保存。</summary>
    public static async Task SaveAsync(
        string filePath, MasterKeyFileDto dto, CancellationToken ct)
    {
        EnsureDirectory(filePath);
        await File.WriteAllTextAsync(filePath, dto.ToJson(), ct).ConfigureAwait(false);
        TryRestrictPermissions(filePath);
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// 收紧文件权限：
    ///   - Windows：ACL 仅当前账户 FullControl，禁用继承
    ///   - Linux / macOS：chmod 600（OwnerRead | OwnerWrite）
    /// 失败静默（不阻塞主流程）。
    /// </summary>
    public static void TryRestrictPermissions(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                RestrictOnWindows(filePath);
            }
            else
            {
                File.SetUnixFileMode(filePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // 权限设置失败不阻塞启动（可能文件系统不支持 / 权限不足）
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictOnWindows(string filePath)
    {
        var fi = new FileInfo(filePath);
        var acl = fi.GetAccessControl();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User;
        if (currentUser is null) return;

        acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            currentUser,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.AccessControlType.Allow));

        fi.SetAccessControl(acl);
    }
}
