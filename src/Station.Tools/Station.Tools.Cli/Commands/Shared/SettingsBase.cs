using System.IO;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Station.Tools.Cli.Commands.Shared;

/// <summary>通用设置基类。</summary>
public abstract class SettingsBase : CommandSettings
{
    [CommandOption("-o|--out <PATH>")]
    [System.ComponentModel.Description("输出路径")]
    public string? Output { get; init; }

    [CommandOption("--json")]
    [System.ComponentModel.Description("以 JSON 格式输出")]
    public bool Json { get; init; }

    [CommandOption("--quiet")]
    [System.ComponentModel.Description("安静模式（仅输出结果）")]
    public bool Quiet { get; init; }
}

/// <summary>输入输出辅助。</summary>
public static class IoHelpers
{
    public static async Task WriteAsync(string? outputPath, string content)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            AnsiConsole.WriteLine(content);
            return;
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(outputPath, content);
        AnsiConsole.MarkupLine($"[green]已写入：[/]{Markup.Escape(outputPath)}");
    }

    public static string ReadInput(string? filePath, string? inlineText)
    {
        if (!string.IsNullOrEmpty(inlineText)) return inlineText;
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath)) return File.ReadAllText(filePath);
        throw new FileNotFoundException("未指定输入内容或文件不存在");
    }

    public static void ShowError(string? code, string? message)
    {
        AnsiConsole.MarkupLine($"[red]✗ 错误[/] [grey]({Markup.Escape(code ?? "-")})[/]");
        AnsiConsole.MarkupLine($"  {Markup.Escape(message ?? "未知错误")}");
    }

    public static void ShowSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");
    }
}
