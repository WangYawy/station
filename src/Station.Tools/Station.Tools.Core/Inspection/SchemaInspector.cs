using Microsoft.Data.Sqlite;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Inspection;

/// <summary>数据库表结构检查。</summary>
public static class SchemaInspector
{
    private static readonly string[] RequiredTables =
    {
        "station_crypto_policy",
        "station_sys_setting",
        "station_collect_task",
        "station_collect_file",
        "station_uploaded_file",
        "station_audit_log"
    };

    public static ToolResult<IReadOnlyList<TableCheck>> CheckSchema(string dbPath)
    {
        try
        {
            if (!File.Exists(dbPath))
                return ToolResult<IReadOnlyList<TableCheck>>.Fail("DB_NOT_FOUND", $"数据库不存在：{dbPath}");

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            var result = new List<TableCheck>();
            foreach (var table in RequiredTables)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@Name";
                cmd.Parameters.AddWithValue("@Name", table);

                var exists = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                result.Add(new TableCheck(table, exists));
            }

            return ToolResult<IReadOnlyList<TableCheck>>.Ok(result);
        }
        catch (Exception ex)
        {
            return ToolResult<IReadOnlyList<TableCheck>>.Fail("CHECK_FAIL", ex.Message);
        }
    }
}

/// <summary>表检查结果。</summary>
public sealed record TableCheck(string TableName, bool Exists);
