namespace Station.Infrastructure.Backup;

/// <summary>备份配置，对应 <c>Station:Backup</c>：本地库每日备份保留 7 份，平台默认 30 份（可配）。</summary>
public sealed class BackupOptions
{
    public const string SectionName = "Station:Backup";

    public bool Enabled { get; set; } = true;

    public string BackupDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Station", "backups");

    public int RetentionCount { get; set; } = 7;

    public int Hour { get; set; } = 3;

    public int Minute { get; set; } = 0;

    /// <summary>逻辑导出命令路径（默认 mysqldump / pg_dump / sys_dump）。</summary>
    public string? DumpCommandTemplate { get; set; }
}
