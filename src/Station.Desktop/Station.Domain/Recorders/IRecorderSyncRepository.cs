namespace Station.Domain.Recorders;

/// <summary>长连接、后台批量场景专用（平台同步 / 远程指令）。</summary>
public interface IRecorderSyncRepository
{
    Recorder? GetById(long id);
    Recorder? GetBySerial(string serialNumber);
    Recorder? GetByCode(string code);
    Recorder? GetByName(string name);
    IReadOnlyList<Recorder> GetList();
    void Insert(Recorder recorder);
    void Update(Recorder recorder);
    void UpdateRange(IEnumerable<Recorder> recorders);
}
