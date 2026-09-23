using Spectre.Console;
using Spectre.Console.Cli;
using Station.Crypto.KeyGen;
using Station.Tools.Cli.Commands.Shared;
using Station.Tools.Core.Licensing;

namespace Station.Tools.Cli.Commands;

// ============ 生成授权 ============

public sealed class LicenseGenerateCommand : AsyncCommand<LicenseGenerateCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("--station <CODE>")] public string StationCode { get; init; } = "ST0001";
        [CommandOption("--fingerprint <FP>")] public string Fingerprint { get; init; } = string.Empty;
        [CommandOption("--expires <DATE>")] public string Expires { get; init; } = string.Empty;
        [CommandOption("--product <CODE>")] public string ProductCode { get; init; } = "STATION-DESKTOP-1";
        [CommandOption("-k|--private-key <PATH>")] public string? PrivateKey { get; init; }
        [CommandOption("-m|--master <PATH>")] public string? MasterKeyFile { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.PrivateKey) || !File.Exists(settings.PrivateKey) ||
            string.IsNullOrEmpty(settings.MasterKeyFile) || !File.Exists(settings.MasterKeyFile))
        {
            IoHelpers.ShowError("NO_INPUT", "需要 -k（私钥）和 -m（主密钥文件）");
            return 1;
        }

        if (string.IsNullOrEmpty(settings.Fingerprint))
        {
            IoHelpers.ShowError("NO_FINGERPRINT", "请用 --fingerprint 指定硬件指纹（可用 license fingerprint 获取）");
            return 1;
        }

        if (!DateTime.TryParse(settings.Expires, out var expires))
        {
            IoHelpers.ShowError("INVALID_DATE", $"无效的到期时间：{settings.Expires}");
            return 1;
        }

        var privPem = await File.ReadAllTextAsync(settings.PrivateKey);
        var masterKey = MasterKeyFile.FromJson(await File.ReadAllTextAsync(settings.MasterKeyFile));

        var result = LicenseBuilder.Generate(
            settings.StationCode, settings.Fingerprint, expires,
            settings.ProductCode, privPem, masterKey);

        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        await IoHelpers.WriteAsync(settings.Output ?? "license.lic", result.Data!);
        IoHelpers.ShowSuccess("授权文件已生成");
        return 0;
    }
}

// ============ 验证授权 ============

public sealed class LicenseVerifyCommand : AsyncCommand<LicenseVerifyCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-i|--in <PATH>")] public string? Input { get; init; }
        [CommandOption("-k|--public-key <PATH>")] public string? PublicKey { get; init; }
        [CommandOption("-m|--master <PATH>")] public string? MasterKeyFile { get; init; }
        [CommandOption("--station <CODE>")] public string? ExpectedStation { get; init; }
        [CommandOption("--fingerprint <FP>")] public string? ExpectedFingerprint { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Input) || !File.Exists(settings.Input) ||
            string.IsNullOrEmpty(settings.PublicKey) || !File.Exists(settings.PublicKey) ||
            string.IsNullOrEmpty(settings.MasterKeyFile) || !File.Exists(settings.MasterKeyFile))
        {
            IoHelpers.ShowError("NO_INPUT", "需要 -i / -k / -m");
            return 1;
        }

        var licenseJson = await File.ReadAllTextAsync(settings.Input);
        var pubPem = await File.ReadAllTextAsync(settings.PublicKey);
        var masterKey = MasterKeyFile.FromJson(await File.ReadAllTextAsync(settings.MasterKeyFile));

        var result = LicenseValidator.Validate(
            licenseJson, pubPem, masterKey,
            settings.ExpectedStation, settings.ExpectedFingerprint);

        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var check = result.Data!;
        if (check.Valid)
        {
            IoHelpers.ShowSuccess($"授权有效：{check.Message}");
            if (check.Payload is not null)
            {
                AnsiConsole.MarkupLine($"  授权编号：{Markup.Escape(check.Payload.LicenseKey)}");
                AnsiConsole.MarkupLine($"  到期时间：{check.Payload.ExpiresAt:yyyy-MM-dd}");
            }
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]✗ 授权无效：{Markup.Escape(check.Message)}[/]");
        return 2;
    }
}

// ============ 查看授权 ============

public sealed class LicenseInspectCommand : AsyncCommand<LicenseInspectCommand.Settings>
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

        var licenseJson = await File.ReadAllTextAsync(settings.Input);
        var masterKey = MasterKeyFile.FromJson(await File.ReadAllTextAsync(settings.MasterKeyFile));

        var result = LicenseInspector.Inspect(licenseJson, masterKey);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var p = result.Data!;
        var table = new Table().Border(TableBorder.Rounded).AddColumn("属性").AddColumn("值");
        table.AddRow("授权编号", p.LicenseKey);
        table.AddRow("产品编号", p.ProductCode);
        table.AddRow("站点编号", p.StationCode);
        table.AddRow("硬件指纹", p.Fingerprint);
        table.AddRow("签发时间", p.IssuedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        table.AddRow("到期时间", p.ExpiresAt.ToString("yyyy-MM-dd HH:mm:ss"));

        var daysLeft = (int)(p.ExpiresAt - DateTime.UtcNow).TotalDays;
        table.AddRow("剩余天数", daysLeft > 0 ? daysLeft.ToString() : $"[red]已过期 {(-daysLeft)} 天[/]");

        AnsiConsole.Write(table);
        return 0;
    }
}

// ============ 本机指纹 ============

public sealed class LicenseFingerprintCommand : Command<LicenseFingerprintCommand.Settings>
{
    public sealed class Settings : SettingsBase { }

    public override int Execute(CommandContext context, Settings settings)
    {
        var result = MachineFingerprintTool.Collect();
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        if (settings.Json)
        {
            AnsiConsole.WriteLine($"{{\"fingerprint\":\"{result.Data}\"}}");
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]本机硬件指纹：[/]");
        AnsiConsole.WriteLine(result.Data!);
        return 0;
    }
}
