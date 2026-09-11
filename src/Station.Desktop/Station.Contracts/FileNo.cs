namespace Station.Contracts;

/// <summary>
/// 文件业务编号规则：{采集站编号}-{本地文件ID}。
/// 采集站编号全局唯一 + 本地文件 ID 站内唯一（记录不物理删除），组合全局唯一。
/// </summary>
public static class FileNo
{
    public static string Create(string stationCode, long localFileId) => $"{stationCode}-{localFileId}";
}
