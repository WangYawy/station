namespace Station.Infrastructure.IdGenerators;

/// <summary>跨库 ID 生成器（雪花算法，long 主键显式赋值，四库通用）。</summary>
public interface IIdGenerator
{
    long NextId();
}
