using SqlSugar;
using Station.Domain.Enums;

namespace Station.Domain.Entities;

/// <summary>
/// 采集文件台账（本地采集阶段记录）。
/// 
/// 【摘要与签名】
///   - ContentDigest 永远是明文摘要；
///   - LocalEncrypted=true 表示本地缓存已加密；
///   - EncryptedSize 是加密后的字节数（Ciphertext 模式下远端 size 校验用）。
/// </summary>
[SugarTable("station_collect_file")]
public sealed class CollectFile
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    /// <summary>所属采集任务 ID。</summary>
    public long TaskId { get; set; }

    /// <summary>文件名（不含路径）。</summary>
    [SugarColumn(Length = 255)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>文件扩展名（含点，小写）。</summary>
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? Extension { get; set; }

    /// <summary>相对于记录仪根目录的相对路径。</summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? RelativePath { get; set; }

    /// <summary>文件大小（明文，字节）。</summary>
    public long Size { get; set; }

    /// <summary>文件指纹（记录仪端生成，用于去重）。</summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? Fingerprint { get; set; }

    /// <summary>
    /// 文件明文的摘要（Base64）。算法由 DigestAlgorithm 标识。
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = true)]
    public string? ContentDigest { get; set; }

    /// <summary>摘要算法标识（"SM3" / "SHA-256"）。</summary>
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? DigestAlgorithm { get; set; }

    /// <summary>元数据签名（Base64）。</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? Signature { get; set; }

    /// <summary>签名算法标识（"SM2-SM3" / "RSA-SHA256"）。</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SignatureAlgorithm { get; set; }

    /// <summary>本地缓存文件是否已加密。</summary>
    public bool LocalEncrypted { get; set; }

    /// <summary>加密后的字节数（含 STFE 头部 + 分块开销）。</summary>
    public long EncryptedSize { get; set; }

    /// <summary>采集状态。</summary>
    public CollectFileStatus Status { get; set; }

    /// <summary>采集进度（0~1）。</summary>
    public double Progress { get; set; }

    /// <summary>采集速度（字节/秒）。</summary>
    public double SpeedBytesPerSecond { get; set; }

    /// <summary>错误信息。</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? ErrorMessage { get; set; }

    /// <summary>完成采集时间。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? CollectedAt { get; set; }

    /// <summary>记录仪文件原始修改时间。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? OriginalModifiedAt { get; set; }

    // ---------- 上传相关 ----------

    /// <summary>上传状态。</summary>
    public UploadStatus SyncStatus { get; set; }

    /// <summary>上传进度（0~1）。</summary>
    public double UploadProgress { get; set; }

    /// <summary>上传重试次数。</summary>
    public int UploadRetryCount { get; set; }

    /// <summary>远端路径。</summary>
    [SugarColumn(Length = 512, IsNullable = true)]
    public string? RemotePath { get; set; }

    /// <summary>上传错误信息。</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? UploadError { get; set; }

    /// <summary>文件业务编号 = {采集站编号}-{本地文件ID}。</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? FileNo { get; set; }
}
