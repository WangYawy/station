namespace Station.Domain.Licensing;

/// <summary>长连接、后台授权检查场景专用。</summary>
public interface ILicenseSyncRepository
{
    License? GetActive();
    void Update(License license);
    void Insert(License license);
}
