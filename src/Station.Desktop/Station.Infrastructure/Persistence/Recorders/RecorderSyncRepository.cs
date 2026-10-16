using Station.Data.SqlSugar;
using Station.Domain.Recorders;

namespace Station.Infrastructure.Persistence.Recorders;

public sealed class RecorderSyncRepository : IRecorderSyncRepository
{
    private readonly ILoopSqlSugarClient _db;

    public RecorderSyncRepository(ILoopSqlSugarClient db) => _db = db;

    public Recorder? GetById(long id)
    {
        var po = _db.Queryable<RecorderPo>().In(id).First();
        return po is null ? null : RecorderMapper.ToDomain(po);
    }

    public Recorder? GetBySerial(string serialNumber)
    {
        var po = _db.Queryable<RecorderPo>().Where(x => x.SerialNumber == serialNumber).First();
        return po is null ? null : RecorderMapper.ToDomain(po);
    }

    public Recorder? GetByCode(string code)
    {
        var po = _db.Queryable<RecorderPo>().Where(x => x.Code == code).First();
        return po is null ? null : RecorderMapper.ToDomain(po);
    }
    public Recorder? GetByName(string name)
    {
        var po = _db.Queryable<RecorderPo>().Where(x => x.Name == name).First();
        return po is null ? null : RecorderMapper.ToDomain(po);
    }
    public IReadOnlyList<Recorder> GetList()
        => _db.Queryable<RecorderPo>().ToList().Select(RecorderMapper.ToDomain).ToList();

    public void Insert(Recorder recorder)
        => _db.Insertable(RecorderMapper.ToPo(recorder)).ExecuteCommand();

    public void Update(Recorder recorder)
        => _db.Updateable(RecorderMapper.ToPo(recorder)).ExecuteCommand();

    public void UpdateRange(IEnumerable<Recorder> recorders)
        => _db.Updateable(recorders.Select(RecorderMapper.ToPo).ToList()).ExecuteCommand();
}
