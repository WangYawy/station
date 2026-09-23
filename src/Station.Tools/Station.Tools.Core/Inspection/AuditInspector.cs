using Microsoft.Data.Sqlite;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Inspection;

/// <summary>审计日志查询。</summary>
public static class AuditInspector
{
    /// <summary>查询最近审计日志。</summary>
    public static ToolResult<IReadOnlyList<AuditRow>> GetRecent(
        string dbPath,
        int limit = 20,
        string? operationType = null)
    {
        try
        {
            if (!File.Exists(dbPath))
                return ToolResult<IReadOnlyList<AuditRow>>.Fail("DB_NOT_FOUND", $"数据库不存在：{dbPath}");

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = operationType is null
                ? "SELECT CreatedAt, OperatorAccount, OperationType, Target, Detail, Result FROM station_audit_log ORDER BY CreatedAt DESC LIMIT @Limit"
                : "SELECT CreatedAt, OperatorAccount, OperationType, Target, Detail, Result FROM station_audit_log WHERE OperationType = @Type ORDER BY CreatedAt DESC LIMIT @Limit";

            cmd.Parameters.AddWithValue("@Limit", limit);
            if (operationType is not null) cmd.Parameters.AddWithValue("@Type", operationType);

            var rows = new List<AuditRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new AuditRow(
                    Time: reader.GetString(0),
                    Operator: reader.IsDBNull(1) ? "-" : reader.GetString(1),
                    OperationType: reader.GetString(2),
                    Target: reader.IsDBNull(3) ? "-" : reader.GetString(3),
                    Detail: reader.IsDBNull(4) ? "-" : reader.GetString(4),
                    Result: reader.GetInt32(5) == 1 ? "成功" : "失败"));
            }

            return ToolResult<IReadOnlyList<AuditRow>>.Ok(rows);
        }
        catch (Exception ex)
        {
            return ToolResult<IReadOnlyList<AuditRow>>.Fail("QUERY_FAIL", ex.Message);
        }
    }
}

/// <summary>审计行。</summary>
public sealed record AuditRow(
    string Time,
    string Operator,
    string OperationType,
    string Target,
    string Detail,
    string Result);
