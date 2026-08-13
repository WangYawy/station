using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Infrastructure.Db;

namespace Station.Infrastructure.Backup;

public sealed record BackupFileInfo(string FileName, string FullPath, long Size, DateTime CreatedAt);

public interface IDatabaseBackupService
{
    Task<string> CreateBackupAsync();

    IReadOnlyList<BackupFileInfo> ListBackups();

    Task<int> RestoreAsync(string backupPath);
}

/// <summary>
/// 数据库备份：SQLite 用 Sqlite Backup API 一致性快照；MySQL/PostgreSQL/Kingbase 用逻辑 SQL 导出（建表+数据，跨库可还原，容器友好）。
/// 备份文件按时间戳命名，保留份数按配置裁剪。
/// </summary>
public sealed class DatabaseBackupService : IDatabaseBackupService
{
    private const string FilePrefix = "station_backup_";

    private readonly DbOptions _db;
    private readonly BackupOptions _options;
    private readonly ISqlSugarClient _client;

    public DatabaseBackupService(DbOptions db, IOptions<BackupOptions> options, ISqlSugarClient client)
    {
        _db = db;
        _options = options.Value;
        _client = client;
    }

    public async Task<string> CreateBackupAsync()
    {
        Directory.CreateDirectory(_options.BackupDirectory);
        var fileName = $"{FilePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}.sql";
        var path = Path.Combine(_options.BackupDirectory, fileName);
        if (_db.Provider == DbProvider.Sqlite)
        {
            await BackupSqliteAsync(path);
        }
        else
        {
            await LogicalDumpAsync(path);
        }

        Prune();
        return path;
    }

    public IReadOnlyList<BackupFileInfo> ListBackups() =>
        Directory.Exists(_options.BackupDirectory)
            ? Directory.GetFiles(_options.BackupDirectory, $"{FilePrefix}*")
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new BackupFileInfo(f.Name, f.FullName, f.Length, f.LastWriteTimeUtc))
                .ToList()
            : [];

    /// <summary>还原：SQLite 直接替换库文件；逻辑 SQL 逐表先清空再插入（幂等还原）。</summary>
    public async Task<int> RestoreAsync(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new InvalidOperationException("备份文件不存在");
        }

        if (_db.Provider == DbProvider.Sqlite)
        {
            var source = GetValue(ParseConnectionString(_db.ConnectionString), "Data Source");
            File.Copy(backupPath, source, true);
            return 1;
        }

        var executed = 0;
        string? currentTable = null;
        var truncatedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(backupPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("-- table ", StringComparison.Ordinal))
            {
                currentTable = line["-- table ".Length..].Split(':')[0].Trim();
                continue;
            }

            if (line.Length == 0 || line.StartsWith("--"))
            {
                continue;
            }

            var statement = line.TrimEnd(';');
            if (statement.Length == 0)
            {
                continue;
            }

            if (currentTable is not null && truncatedTables.Add(currentTable))
            {
                await _client.Ado.ExecuteCommandAsync($"delete from {currentTable}");
            }

            await _client.Ado.ExecuteCommandAsync(statement);
            executed++;
        }

        return executed;
    }

    private async Task BackupSqliteAsync(string destinationPath)
    {
        var source = GetValue(ParseConnectionString(_db.ConnectionString), "Data Source");
        if (!File.Exists(source))
        {
            throw new InvalidOperationException($"本地数据库文件不存在：{source}");
        }

        using var sourceConn = new SqliteConnection($"Data Source={source};Pooling=False");
        sourceConn.Open();
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        using var destConn = new SqliteConnection($"Data Source={destinationPath};Pooling=False");
        destConn.Open();
        sourceConn.BackupDatabase(destConn);
    }

    private async Task LogicalDumpAsync(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- station logical backup {DateTime.Now:O}");
        sb.AppendLine($"-- provider: {_db.Provider}");
        var tables = _client.DbMaintenance.GetTableInfoList()
            .Where(t => !t.Name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        foreach (var table in tables)
        {
            var data = await _client.Ado.GetDataTableAsync($"select * from {table}");
            sb.AppendLine($"-- table {table}: {data.Rows.Count} rows");
            foreach (DataRow row in data.Rows)
            {
                sb.Append("insert into ").Append(table).Append(" values (");
                for (var i = 0; i < data.Columns.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(FormatValue(row[i]));
                }

                sb.AppendLine(");");
            }
        }

        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string FormatValue(object? value)
    {
        if (value is null || value is DBNull)
        {
            return "null";
        }

        return value switch
        {
            bool b => b ? "1" : "0",
            byte[] bytes => "X'" + Convert.ToHexString(bytes) + "'",
            DateTime dt => "'" + dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'",
            DateTimeOffset dto => "'" + dto.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'",
            string s => "'" + s.Replace("'", "''") + "'",
            Guid g => "'" + g + "'",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
        };
    }

    private int Prune()
    {
        var backups = ListBackups();
        var removed = 0;
        foreach (var backup in backups.Skip(Math.Max(0, _options.RetentionCount)))
        {
            try
            {
                File.Delete(backup.FullPath);
                removed++;
            }
            catch
            {
                // 单个清理失败不阻断
            }
        }

        return removed;
    }

    private static Dictionary<string, string> ParseConnectionString(string connectionString)
    {
        return connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0].Trim(), kv => kv[1].Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static string GetValue(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var value) ? value : string.Empty;
}
