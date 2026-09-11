using Station.Contracts;

namespace Station.Application.Collecting;

/// <summary>扩展名 → FileKind 映射（契约枚举：Video/Audio/Image/Other）。</summary>
public static class FileKindMapper
{
    private static readonly HashSet<string> Video = [".mp4", ".avi", ".flv", ".mov"];
    private static readonly HashSet<string> Audio = [".wav", ".mp3", ".aac"];
    private static readonly HashSet<string> Image = [".jpg", ".bmp", ".jpeg", ".png"];

    public static FileKind Map(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (Video.Contains(extension))
        {
            return FileKind.Video;
        }

        if (Audio.Contains(extension))
        {
            return FileKind.Audio;
        }

        return Image.Contains(extension) ? FileKind.Image : FileKind.Other;
    }
}
