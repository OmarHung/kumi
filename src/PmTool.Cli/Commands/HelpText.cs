using System.Reflection;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Commands;

internal static class HelpText
{
    public static string VersionNumber =>
        typeof(HelpText).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

    public static string Version => $"kumi {VersionNumber}";

    public static async Task WriteOverviewAsync(TextWriter output)
    {
        await output.WriteLineAsync("kumi：Kumi 的命令列工具");
        await output.WriteLineAsync();
        await output.WriteLineAsync("用法：kumi <群組> <指令> [選項]");
        foreach (var group in CommandRegistry.All.GroupBy(command => command.Group))
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync($"{group.Key}：");
            foreach (var command in group)
            {
                await output.WriteLineAsync($"  {command.Path,-22} {command.Summary}");
            }
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("每個指令都可以加 --json（輸出給腳本用的 JSON）與 --server 網址。");
        await output.WriteLineAsync($"環境變數 {Credentials.ServerVariable}、{Credentials.TokenVariable} 優先於設定檔，給 CI 用（舊名 PM_SERVER、PM_TOKEN 也認得）。");
        await output.WriteLineAsync("結束代碼：0 成功、1 一般錯誤、2 驗證失敗或用法不對、3 有需要人決定的資料影響。");
        await output.WriteLineAsync("執行 kumi <群組> <指令> --help 看個別指令的說明。");
    }

    public static async Task WriteGroupAsync(TextWriter output, string group, IEnumerable<CommandSpec> commands)
    {
        await output.WriteLineAsync($"kumi {group} 的指令：");
        foreach (var command in commands)
        {
            await output.WriteLineAsync($"  {command.Path,-22} {command.Summary}");
        }
    }

    public static async Task WriteCommandAsync(TextWriter output, CommandSpec command)
    {
        await output.WriteLineAsync($"{command.Summary}");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"用法：{command.Usage}");
        await output.WriteLineAsync();
        await output.WriteLineAsync("選項：");
        foreach (var option in command.Options.Concat(GlobalOptions.All))
        {
            var shortForm = option.Short is { } letter ? $"-{letter}, " : "    ";
            var name = $"--{option.Name}{(option.TakesValue ? $" <{option.Value}>" : string.Empty)}";
            await output.WriteLineAsync($"  {shortForm}{name,-26} {option.Description}");
        }
    }
}
