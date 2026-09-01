using Station.Application.Recorders;
using Station.Desktop.Infrastructure.Collecting;
using Station.Infrastructure.Collecting;
using Station.Infrastructure.Recorders;

namespace Station.Desktop.Infrastructure.Recorders;

/// <summary>
/// 记录仪根目录文件存取分发：MTP 虚拟根走 WPD，其余（UMS/模拟源）走文件系统。
/// </summary>
public sealed class CompositeRecorderRootFileStore : IRecorderRootFileStore
{
    private readonly IRecorderRootFileStore _mtp;
    private readonly FileSystemRecorderRootFileStore _fileSystem = new();

    public CompositeRecorderRootFileStore(IRecorderRootFileStore mtp)
    {
        _mtp = mtp;
    }

    public string? ReadFile(string recorderRoot, string fileName) =>
        MtpRoot.IsMtpRoot(recorderRoot)
            ? _mtp.ReadFile(recorderRoot, fileName)
            : _fileSystem.ReadFile(recorderRoot, fileName);

    public void WriteFile(string recorderRoot, string fileName, string content)
    {
        if (MtpRoot.IsMtpRoot(recorderRoot))
        {
            _mtp.WriteFile(recorderRoot, fileName, content);
        }
        else
        {
            _fileSystem.WriteFile(recorderRoot, fileName, content);
        }
    }

    public void DeleteFile(string recorderRoot, string fileName)
    {
        if (MtpRoot.IsMtpRoot(recorderRoot))
        {
            _mtp.DeleteFile(recorderRoot, fileName);
        }
        else
        {
            _fileSystem.DeleteFile(recorderRoot, fileName);
        }
    }
}
