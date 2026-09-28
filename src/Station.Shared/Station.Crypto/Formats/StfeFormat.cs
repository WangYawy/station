namespace Station.Crypto.Formats;

/// <summary>STFE 文件格式常量。</summary>
public static class StfeFormat
{
    /// <summary>文件扩展名。</summary>
    public const string Extension = ".stfe";

    /// <summary>文件魔数（头 4 字节）。</summary>
    public const string Magic = "STFE";

    /// <summary>文件头字节数。</summary>
    public const int HeaderSize = StfeHeader.HeaderSize;

    /// <summary>默认分块大小：4 MB。</summary>
    public const int DefaultChunkSize = 4 * 1024 * 1024;
}
