using Microsoft.Data.Sqlite;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Inspection;

/// <summary>加密策略查询。</summary>
public static class PolicyInspector
{
    public static ToolResult<IReadOnlyList<PolicyRow>> GetAll(string dbPath)
    {
        try
        {
            if (!File.Exists(dbPath))
                return ToolResult<IReadOnlyList<PolicyRow>>.Fail("DB_NOT_FOUND", $"数据库不存在：{dbPath}");

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT UsageCode, Algorithm, SecondaryAlgorithm, AllowLegacy, Enabled, Source, UpdatedAt, UpdatedBy FROM station_crypto_policy ORDER BY UsageCode";

            var rows = new List<PolicyRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new PolicyRow(
                    UsageCode: reader.GetString(0),
                    Algorithm: reader.GetString(1),
                    SecondaryAlgorithm: reader.IsDBNull(2) ? null : reader.GetString(2),
                    AllowLegacy: reader.GetInt32(3) == 1,
                    Enabled: reader.GetInt32(4) == 1,
                    Source: reader.IsDBNull(5) ? "Local" : reader.GetString(5),
                    UpdatedAt: reader.IsDBNull(6) ? "-" : reader.GetString(6),
                    UpdatedBy: reader.IsDBNull(7) ? "-" : reader.GetString(7)));
            }

            return ToolResult<IReadOnlyList<PolicyRow>>.Ok(rows);
        }
        catch (Exception ex)
        {
            return ToolResult<IReadOnlyList<PolicyRow>>.Fail("QUERY_FAIL", ex.Message);
        }
    }
}

/// <summary>策略行。</summary>
public sealed record PolicyRow(
    string UsageCode,
    string Algorithm,
    string? SecondaryAlgorithm,
    bool AllowLegacy,
    bool Enabled,
    string Source,
    string UpdatedAt,
    string UpdatedBy);
