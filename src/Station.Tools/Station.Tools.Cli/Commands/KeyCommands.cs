using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Station.Tools.Cli.Commands.Shared;
using Station.Tools.Core.Keys;

namespace Station.Tools.Cli.Commands;

// ============ SM2 密钥生成 ============

public sealed class KeyGenSm2Command : AsyncCommand<KeyGenSm2Command.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("--name <NAME>")]
        [System.ComponentModel.Description("密钥名（用于文件名，默认 license）")]
        public string Name { get; init; } = "license";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var result = Sm2KeyTool.Generate();
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var pair = result.Data!;
        var outDir = settings.Output ?? ".";

        var privPath = Path.Combine(outDir, $"{settings.Name}-private.pem");
        var pubPath = Path.Combine(outDir, $"{settings.Name}-public.pem");

        await IoHelpers.WriteAsync(privPath, pair.PrivatePem);
        await IoHelpers.WriteAsync(pubPath, pair.PublicPem);

        IoHelpers.ShowSuccess($"SM2 密钥对已生成");
        AnsiConsole.MarkupLine($"  私钥：[grey]{Markup.Escape(privPath)}[/]");
        AnsiConsole.MarkupLine($"  公钥：[grey]{Markup.Escape(pubPath)}[/]");
        AnsiConsole.MarkupLine("[yellow]⚠ 私钥必须严格保密[/]");

        return 0;
    }
}

// ============ RSA 密钥生成 ============

public sealed class KeyGenRsaCommand : AsyncCommand<KeyGenRsaCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("--bits <BITS>")]
        [System.ComponentModel.Description("密钥位数（2048/3072/4096）")]
        public int Bits { get; init; } = 2048;

        [CommandOption("--name <NAME>")]
        public string Name { get; init; } = "rsa";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var result = RsaKeyTool.Generate(settings.Bits);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var pair = result.Data!;
        var outDir = settings.Output ?? ".";

        await IoHelpers.WriteAsync(Path.Combine(outDir, $"{settings.Name}-private.pem"), pair.PrivatePem);
        await IoHelpers.WriteAsync(Path.Combine(outDir, $"{settings.Name}-public.pem"), pair.PublicPem);

        IoHelpers.ShowSuccess($"RSA-{settings.Bits} 密钥对已生成");
        return 0;
    }
}

// ============ 主密钥生成 ============

public sealed class KeyGenMasterCommand : AsyncCommand<KeyGenMasterCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("--count <COUNT>")]
        [System.ComponentModel.Description("生成密钥版本数量")]
        public int Count { get; init; } = 1;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var result = MasterKeyTool.Generate(settings.Count);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var path = settings.Output ?? "master.key";
        await IoHelpers.WriteAsync(path, result.Data!);

        IoHelpers.ShowSuccess($"主密钥文件已生成（{settings.Count} 个版本）");
        AnsiConsole.MarkupLine("[yellow]⚠ 立即备份并设置文件权限（Windows ACL / Linux 600）[/]");

        return 0;
    }
}

// ============ 密钥检查 ============

public sealed class KeyInspectCommand : AsyncCommand<KeyInspectCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-i|--in <PATH>")]
        [System.ComponentModel.Description("PEM 文件路径")]
        public string? Input { get; init; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Input) || !File.Exists(settings.Input))
        {
            IoHelpers.ShowError("NO_INPUT", "请用 -i 指定 PEM 文件");
            return Task.FromResult(1);
        }

        var pem = File.ReadAllText(settings.Input);
        var result = KeyInspectTool.Inspect(pem);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return Task.FromResult(1);
        }

        var info = result.Data!;

        if (settings.Json)
        {
            AnsiConsole.WriteLine(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
            return Task.FromResult(0);
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("属性")
            .AddColumn("值");
        table.AddRow("类型", info.Type);
        table.AddRow("种类", info.Kind);
        table.AddRow("位数", info.Bits.ToString());
        table.AddRow("SM3 指纹", info.Fingerprint);

        AnsiConsole.Write(table);
        return Task.FromResult(0);
    }
}

// ============ 密钥格式转换 ============

public sealed class KeyConvertCommand : AsyncCommand<KeyConvertCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-i|--in <PATH>")]
        public string? Input { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [System.ComponentModel.Description("输出格式：pem/base64/hex/der")]
        public string Format { get; init; } = "base64";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Input) || !File.Exists(settings.Input))
        {
            IoHelpers.ShowError("NO_INPUT", "请用 -i 指定输入文件");
            return 1;
        }

        var pem = await File.ReadAllTextAsync(settings.Input);
        var format = settings.Format.ToLowerInvariant() switch
        {
            "pem" => KeyOutputFormat.Pem,
            "base64" or "b64" => KeyOutputFormat.Base64,
            "hex" => KeyOutputFormat.Hex,
            "der" or "der-base64" => KeyOutputFormat.DerBase64,
            _ => (KeyOutputFormat?)null
        };

        if (format is null)
        {
            IoHelpers.ShowError("INVALID_FORMAT", $"不支持的格式：{settings.Format}");
            return 1;
        }

        var result = KeyConvertTool.Convert(pem, format.Value);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        await IoHelpers.WriteAsync(settings.Output, result.Data!);
        return 0;
    }
}

// ============ 密钥指纹 ============

public sealed class KeyFingerprintCommand : AsyncCommand<KeyFingerprintCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-i|--in <PATH>")]
        public string? Input { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Input) || !File.Exists(settings.Input))
        {
            IoHelpers.ShowError("NO_INPUT", "请用 -i 指定 PEM 文件");
            return 1;
        }

        var pem = await File.ReadAllTextAsync(settings.Input);
        var result = KeyInspectTool.Fingerprint(pem);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        AnsiConsole.WriteLine(result.Data!);
        return 0;
    }
}
