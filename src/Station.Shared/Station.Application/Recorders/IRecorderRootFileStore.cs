namespace Station.Application.Recorders;

/// <summary>
/// 记录仪根目录上的文件存取抽象：UMS 走文件系统，MTP 走 WPD 内容 API。
/// 用于读写 <c>station_bind.ini</c> 绑定文件。
/// </summary>
public interface IRecorderRootFileStore
{
    /// <summary>读取根目录文件；不存在返回 null。</summary>
    string? ReadFile(string recorderRoot, string fileName);

    /// <summary>写入（覆盖）根目录文件。</summary>
    void WriteFile(string recorderRoot, string fileName, string content);

    /// <summary>删除根目录文件；不存在时静默忽略。</summary>
    void DeleteFile(string recorderRoot, string fileName);
}
