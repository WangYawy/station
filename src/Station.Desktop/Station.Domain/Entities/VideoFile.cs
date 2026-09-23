using SqlSugar;
using Station.Contracts;
using TaskStatus = Station.Contracts.TaskStatus;

namespace Station.Domain.Entities;

/// <summary>
/// 已上传文件台账（采集站本地文件成功上传到远端后的元数据索引）。
/// 
/// 表名：station_uploaded_file
/// 业务编号：{采集站编号}-{本地文件ID}
/// 
/// 【语义】
///   - 对应"采集 → 上传"流程中的最终态；
///   - ContentDigest 永远是"明文内容摘要"，与远端是否存密文无关；
///   - RemoteContentMode 记录本次上传是明文还是密文；
///   - Signature 是对元数据包的签名，保证来源真实性。
/// 
/// 【本地记录不物理删除】，保证本地 ID 不复用。
/// </summary>
[SugarTable("station_uploaded_file")]
[SugarIndex("uk_uploadedfile_fileno", nameof(FileNo), OrderByType.Asc, true)]
public sealed class UploadedFile
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    /// <summary>采集站本地文件 ID（指向 station_collect_file.Id）。</summary>
    public long LocalFileId { get; set; }

    /// <summary>文件业务编号（跨端引用）。</summary>
    [SugarColumn(Length = 64)]
    public string FileNo { get; set; } = string.Empty;

    [SugarColumn(Length = 255)]
    public string FileName { get; set; } = string.Empty;

    public long Size { get; set; }

    public FileKind Kind { get; set; }

    /// <summary>
    /// 文件明文的摘要（Base64）。算法由 DigestAlgorithm 标识。
    /// 【注意】这是对明文算的，与远端存密文无关。
    /// </summary>
    [SugarColumn(Length = 128)]
    public string ContentDigest { get; set; } = string.Empty;

    /// <summary>摘要算法标识（"SM3" / "SHA-256"）。</summary>
    [SugarColumn(Length = 32)]
    public string DigestAlgorithm { get; set; } = string.Empty;

    /// <summary>
    /// 元数据签名（Base64）。对规范化 JSON 元数据包的签名。
    /// null 表示未启用文件签名（EnableFileSignature=false）。
    /// </summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? Signature { get; set; }

    /// <summary>签名算法标识（"SM2-SM3" / "RSA-SHA256"）。</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SignatureAlgorithm { get; set; }

    /// <summary>
    /// 远端内容模式："Plaintext" / "Ciphertext"。
    /// 记录本次上传时远端存的是明文还是密文。
    /// </summary>
    [SugarColumn(Length = 16)]
    public string RemoteContentMode { get; set; } = "Plaintext";

    /// <summary>采集时间（采集站系统时间）。</summary>
    public DateTime CollectedAt { get; set; }

    /// <summary>记录仪文件原始时间，单独存储、不覆盖。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? OriginalTime { get; set; }

    /// <summary>归属用户（人员）ID。</summary>
    [SugarColumn(IsNullable = true)]
    public long? UserId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    public TaskStatus Status { get; set; }
}
