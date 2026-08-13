using Microsoft.AspNetCore.Mvc;
using Station.Application.Collecting;
using Station.Domain.Entities;
using Station.Infrastructure.Repositories;

namespace Station.Desktop.WebHost.Controllers;

/// <summary>
/// 采集站内置 Web 文件流端点：按 FileNo 返回本地缓存文件（支持 Range，H.264 在线预览/平台代理）。
/// 访问控制：默认仅本机；开启局域网访问后需登录认证（后续里程碑）。
/// </summary>
[ApiController]
[Route("api/v1/files")]
public class FileStreamController : ControllerBase
{
    private readonly IRepository<CollectFile> _files;
    private readonly IRepository<CollectTask> _tasks;
    private readonly CollectOptions _collectOptions;

    public FileStreamController(
        IRepository<CollectFile> files,
        IRepository<CollectTask> tasks,
        CollectOptions collectOptions)
    {
        _files = files;
        _tasks = tasks;
        _collectOptions = collectOptions;
    }

    [HttpGet("{fileNo}/stream")]
    public async Task<IActionResult> Stream(string fileNo)
    {
        var file = await _files.FirstAsync(f => f.FileNo == fileNo);
        if (file is null)
        {
            return NotFound(new { message = "文件不存在" });
        }

        var task = await _tasks.GetByIdAsync(file.TaskId);
        if (task is null)
        {
            return NotFound(new { message = "任务不存在" });
        }

        var path = Path.Combine(_collectOptions.CacheDirectory, task.TaskNo, file.RelativePath);
        if (!System.IO.File.Exists(path))
        {
            return NotFound(new { message = "本地缓存文件缺失" });
        }

        return PhysicalFile(path, ContentTypeFor(file.FileName), enableRangeProcessing: true);
    }

    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".avi" => "video/x-msvideo",
        ".flv" => "video/x-flv",
        ".mov" => "video/quicktime",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".bmp" => "image/bmp",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".aac" => "audio/aac",
        _ => "application/octet-stream"
    };
}
