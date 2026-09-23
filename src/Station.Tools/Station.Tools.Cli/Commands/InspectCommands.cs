using Spectre.Console;
using Spectre.Console.Cli;
using Station.Tools.Cli.Commands.Shared;
using Station.Tools.Core.Inspection;

namespace Station.Tools.Cli.Commands;

// ============ 审计查询 ============

public sealed class InspectAuditCommand : Command<InspectAuditCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-d|--db <PATH>")] public string DbPath { get; init; } = "station.db";
        [CommandOption("-n|--last <N>")] public int Last { get; init; } = 20;
        [CommandOption("--type <TYPE>")] public string? OperationType { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var result = AuditInspector.GetRecent(settings.DbPath, settings.Last, settings.OperationType);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("时间")
            .AddColumn("操作人")
            .AddColumn("类型")
            .AddColumn("目标")
            .AddColumn("详情")
            .AddColumn("结果");

        foreach (var row in result.Data!)
        {
            table.AddRow(
                Markup.Escape(row.Time),
                Markup.Escape(row.Operator),
                Markup.Escape(row.OperationType),
                Markup.Escape(row.Target),
                Markup.Escape(row.Detail),
                row.Result == "成功" ? $"[green]{row.Result}[/]" : $"[red]{row.Result}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

// ============ 策略查询 ============

public sealed class InspectPolicyCommand : Command<InspectPolicyCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-d|--db <PATH>")] public string DbPath { get; init; } = "station.db";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var result = PolicyInspector.GetAll(settings.DbPath);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("用途")
            .AddColumn("主算法")
            .AddColumn("组合算法")
            .AddColumn("允许旧算法")
            .AddColumn("来源");

        foreach (var row in result.Data!)
        {
            table.AddRow(
                Markup.Escape(row.UsageCode),
                Markup.Escape(row.Algorithm),
                Markup.Escape(row.SecondaryAlgorithm ?? "-"),
                row.AllowLegacy ? "是" : "否",
                Markup.Escape(row.Source));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

// ============ 密文格式判断 ============

public sealed class InspectCipherCommand : Command<InspectCipherCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-t|--text <TEXT>")] public string? Text { get; init; }
        [CommandOption("-i|--in <PATH>")] public string? Input { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var text = settings.Text;
        if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(settings.Input) && File.Exists(settings.Input))
            text = File.ReadAllText(settings.Input).Trim();

        if (string.IsNullOrEmpty(text))
        {
            IoHelpers.ShowError("NO_INPUT", "需要 -t 或 -i");
            return 1;
        }

        var result = CipherInspector.Inspect(text);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var info = result.Data!;
        var table = new Table().Border(TableBorder.Rounded).AddColumn("属性").AddColumn("值");
        table.AddRow("类型", info.Kind);
        if (info.Version.HasValue) table.AddRow("版本", $"v{info.Version}");
        if (info.Algorithm is not null) table.AddRow("算法", info.Algorithm);
        AnsiConsole.Write(table);
        return 0;
    }
}

// ============ 表结构检查 ============

public sealed class InspectSchemaCommand : Command<InspectSchemaCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-d|--db <PATH>")] public string DbPath { get; init; } = "station.db";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var result = SchemaInspector.CheckSchema(settings.DbPath);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var table = new Table().Border(TableBorder.Rounded).AddColumn("表名").AddColumn("状态");
        var allOk = true;
        foreach (var row in result.Data!)
        {
            if (!row.Exists) allOk = false;
            table.AddRow(
                Markup.Escape(row.TableName),
                row.Exists ? "[green]✓ 存在[/]" : "[red]✗ 缺失[/]");
        }
        AnsiConsole.Write(table);

        return allOk ? 0 : 2;
    }
}
