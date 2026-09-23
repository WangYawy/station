namespace Station.Crypto.Formats;

/// <summary>STFE 格式常量。</summary>
public static class StfeFormat
{
    public const string Extension = ".stfe";
    public const string Magic = "STFE";
    public const int HeaderSize = StfeHeader.HeaderSize;
    public const int DefaultChunkSize = StfeFileEncryptor.DefaultChunkSize;
}
