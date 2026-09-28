using Spectre.Console;
using Spectre.Console.Cli;
using Station.Crypto.Engine.Keys;
using Station.Crypto.Engine.Models;
using Station.Tools.Cli.Commands.Shared;
using Station.Tools.Core.Binding;
using Station.Tools.Core.Models;

namespace Station.Tools.Cli.Commands;

// ============ 生成绑定 ============

public sealed class BindingGenerateCommand : AsyncCommand<BindingGenerateCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("--serial <SN>")] public string Serial { get; init; } = string.Empty;
        [CommandOption("--model <MODEL>")] public string Model { get; init; } = string.Empty;
        [CommandOption("--user-no <NO>")] public string UserNo { get; init; } = string.Empty;
        [CommandOption("--user-name <NAME>")] public string UserName { get; init; } = string.Empty;
        [CommandOption("--dept-code <CODE>")] public string DeptCode { get; init; } = string.Empty;
        [CommandOption("--dept-name <NAME>")] public string DeptName { get; init; } = string.Empty;
        [CommandOption("-m|--master <PATH>")] public string? MasterKeyFile { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Serial) || string.IsNullOrEmpty(settings.UserNo) ||
            string.IsNullOrEmpty(settings.MasterKeyFile) || !File.Exists(settings.MasterKeyFile))
        {
            IoHelpers.ShowError("NO_INPUT", "需要 --serial / --user-no / --master");
            return 1;
        }

        var keyFile = MasterKeyFileDto.FromJson(await File.ReadAllTextAsync(settings.MasterKeyFile));

        var info = new BindingInfo(
            settings.Serial, settings.Model, settings.UserNo, settings.UserName,
            settings.DeptCode, settings.DeptName, DateTime.Now, string.Empty);

        var result = BindingFileTool.Generate(info, keyFile);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        await IoHelpers.WriteAsync(settings.Output ?? "station_bind.ini", result.Data!);
        IoHelpers.ShowSuccess("绑定文件已生成");
        return 0;
    }
}

// ============ 验证绑定 ============

public sealed class BindingVerifyCommand : AsyncCommand<BindingVerifyCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-i|--in <PATH>")] public string? Input { get; init; }
        [CommandOption("-m|--master <PATH>")] public string? MasterKeyFile { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Input) || !File.Exists(settings.Input) ||
            string.IsNullOrEmpty(settings.MasterKeyFile) || !File.Exists(settings.MasterKeyFile))
        {
            IoHelpers.ShowError("NO_INPUT", "需要 -i 和 -m");
            return 1;
        }

        var content = await File.ReadAllTextAsync(settings.Input);
        var keyFile = MasterKeyFileDto.FromJson(await File.ReadAllTextAsync(settings.MasterKeyFile));

        var result = BindingFileTool.Validate(content, keyFile);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        if (result.Data)
        {
            IoHelpers.ShowSuccess("绑定文件有效");
            return 0;
        }

        AnsiConsole.MarkupLine("[red]✗ 绑定文件无效或被篡改[/]");
        return 2;
    }
}

// ============ 查看绑定 ============

public sealed class BindingInspectCommand : AsyncCommand<BindingInspectCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-i|--in <PATH>")] public string? Input { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Input) || !File.Exists(settings.Input))
        {
            IoHelpers.ShowError("NO_INPUT", "请用 -i 指定绑定文件");
            return 1;
        }

        var content = await File.ReadAllTextAsync(settings.Input);
        var result = BindingFileTool.Inspect(content);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var b = result.Data!;
        var table = new Table().Border(TableBorder.Rounded).AddColumn("属性").AddColumn("值");
        table.AddRow("记录仪序列号", b.DeviceSerial);
        table.AddRow("型号", b.DeviceModel);
        table.AddRow("用户工号", b.UserNo);
        table.AddRow("用户名", b.UserName);
        table.AddRow("部门编码", b.DeptCode);
        table.AddRow("部门名", b.DeptName);
        table.AddRow("绑定时间", b.BoundAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AnsiConsole.Write(table);
        return 0;
    }
}
