using Station.Application.Recorders;

namespace Station.Infrastructure.Recorders;

/// <summary>默认文件系统实现（UMS / 模拟源根目录）。</summary>
public sealed class FileSystemRecorderRootFileStore : IRecorderRootFileStore
{
    public string? ReadFile(string recorderRoot, string fileName)
    {
        var path = Path.Combine(recorderRoot, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void WriteFile(string recorderRoot, string fileName, string content)
    {
        Directory.CreateDirectory(recorderRoot);
        File.WriteAllText(Path.Combine(recorderRoot, fileName), content);
    }

    public void DeleteFile(string recorderRoot, string fileName)
    {
        var path = Path.Combine(recorderRoot, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
