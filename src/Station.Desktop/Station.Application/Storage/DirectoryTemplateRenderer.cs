namespace Station.Application.Storage;

using System.Text.RegularExpressions;


/// <summary>
/// 上传目录模板渲染
/// </summary>
public interface IDirectoryTemplateRenderer
{
    string Render(string template, string stationNo, DateTime date, string recorderName,
        long? userId, long? deptId, string fileType);
}

public sealed class CircuitBreakerOpenException : InvalidOperationException
{
    public CircuitBreakerOpenException(string targetName)
        : base($"存储目标 {targetName} 熔断器已打开，拒绝上传") { }
}


/// <summary>上传目录模板渲染：{StationNo}/{Date}/{RecorderName}/{UserId}/{DeptId}/{FileType}。</summary>
public class DirectoryTemplateRenderer : IDirectoryTemplateRenderer
{
    public string Render(
        string template,
        string stationNo,
        DateTime date,
        string recorderName,
        long? userId,
        long? deptId,
        string fileType)
    {
        var result = Regex.Replace(
            template,
            @"\{Date(:[^}]*)?\}",
            m => date.ToString(m.Groups[1].Success && m.Groups[1].Value.Length > 1
                ? m.Groups[1].Value[1..]
                : "yyyy-MM-dd"));

        result = result
            .Replace("{StationNo}", Sanitize(stationNo))
            .Replace("{RecorderName}", Sanitize(recorderName))
            .Replace("{UserId}", (userId ?? 0).ToString())
            .Replace("{DeptId}", (deptId ?? 0).ToString())
            .Replace("{FileType}", Sanitize(fileType));

        var parts = result.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join("/", parts);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
