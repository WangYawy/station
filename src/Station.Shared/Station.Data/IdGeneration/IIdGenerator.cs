namespace Station.Data.IdGeneration;

/// <summary>ID 生成器（雪花 / GUID / ULID 等实现可替换）。</summary>
public interface IIdGenerator
{
    /// <summary>生成一个全局唯一 ID。</summary>
    long NewId();
}
