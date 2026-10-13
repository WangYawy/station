namespace Station.Data.SoftDelete;

/// <summary>软删除契约。实现此接口的持久化模型可被 RepositoryBase 软删除。</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    long? DeletedBy { get; set; }
}
