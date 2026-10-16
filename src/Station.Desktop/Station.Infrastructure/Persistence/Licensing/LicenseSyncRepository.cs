using Station.Data.SqlSugar;
using Station.Domain.Licensing;

namespace Station.Infrastructure.Persistence.Licensing;

public sealed class LicenseSyncRepository : ILicenseSyncRepository
{
    private readonly ILoopSqlSugarClient _db;
    public LicenseSyncRepository(ILoopSqlSugarClient db) => _db = db;

    public License? GetActive()
    {
        var po = _db.Queryable<LicensePo>().Where(x => x.IsActive).First();
        return po is null ? null : LicenseMapper.ToDomain(po);
    }

    public void Update(License license)
        => _db.Updateable(LicenseMapper.ToPo(license)).ExecuteCommand();

    public void Insert(License license)
        => _db.Insertable(LicenseMapper.ToPo(license)).ExecuteCommand();
}
