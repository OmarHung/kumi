using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;
using PmTool.Cli.Output;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Commands;

/// <summary>kumi update：從登入的伺服器下載新版的單一執行檔，換掉自己。</summary>
internal static class UpdateCommand
{
    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("update", string.Empty, "從伺服器下載新版的 kumi，換掉這個執行檔", "kumi update [--check] [--json]",
            [new OptionSpec("check", "", "只說有沒有新版，不下載")], 0, 0, RunAsync),
    ];

    private static async Task<int> RunAsync(CommandCall call)
    {
        var environment = call.Context.Update;
        var data = await call.Api.GetAsync("cli", null, call.CancellationToken);
        var latest = data.Text("version")
            ?? throw new CliException(ExitCodes.Error, $"伺服器 {call.Api.Server} 沒有提供 kumi 的下載。");
        var current = HelpText.VersionNumber;
        var comparison = Compare(latest, current);
        if (comparison <= 0)
        {
            return await ReportAsync(call, current, latest, available: false, updated: false, comparison == 0
                ? $"已經是最新版（kumi {current}）。"
                : $"伺服器上的是 {latest}，比這份（{current}）舊，不更新。");
        }

        if (call.Arguments.Has("check"))
        {
            return await ReportAsync(call, current, latest, available: true, updated: false,
                $"有新版：{latest}（目前是 {current}）。執行 kumi update 更新。");
        }

        if (!environment.IsStandalone)
        {
            throw new CliException(ExitCodes.Error,
                "這份 kumi 是用 dotnet tool 裝的，不能自己換掉自己。請用 dotnet tool update --global PmTool.Cli --add-source <資料夾> 更新，"
                + "或到網頁「我的帳號 → 命令列工具」下載執行檔。");
        }

        if (Credentials.IsInsecureRemote(call.Api.Server))
        {
            // 中間人可以同時改目錄的 sha256 與檔案，核對就沒有意義，試跑等於執行他給的程式；--insecure 只管權杖，不管這個。
            throw new CliException(ExitCodes.Error,
                $"伺服器 {call.Api.Server} 不是 https，不更新：更新要經過 https（或本機），以免下載的執行檔在路上被換掉。"
                + "請改用 https 的網址，或到網頁「我的帳號 → 命令列工具」下載。");
        }

        var (platform, package) = ChoosePackage(environment, data.List("packages"));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(call.CancellationToken);
        // 第一次 Ctrl-C 讓安裝流程收尾（刪掉暫存檔）；第二次照預設直接結束。
        using var registration = call.Context.RegisterInterrupt(() => stop.Cancel());
        await UpdateInstaller.InstallAsync(call, environment, platform, package, latest, stop.Token);
        return await ReportAsync(call, current, latest, available: true, updated: true, $"已更新到 kumi {latest}（原本是 {current}）。");
    }

    /// <summary>照版本的數字比，不是照字串（0.10.0 比 0.9.0 新）；預備版的後綴不參與比較。</summary>
    private static int Compare(string latest, string current) =>
        Parse(latest, strict: true).CompareTo(Parse(current, strict: false));

    private static Version Parse(string text, bool strict)
    {
        var numbers = text.Split('-', '+')[0];
        if (Version.TryParse(numbers, out var version))
        {
            return version;
        }

        return strict
            ? throw new CliException(ExitCodes.Error, $"伺服器回的版本不對：{text}。")
            : new Version(0, 0);
    }

    private static (string Platform, JsonElement Package) ChoosePackage(UpdateEnvironment environment, IReadOnlyList<JsonElement> packages)
    {
        if (environment.Os == UpdateOs.Other)
        {
            throw new CliException(ExitCodes.Error, "只有 macOS 與 Windows 有提供 kumi 的執行檔。Linux 請用 dotnet tool 更新。");
        }

        foreach (var platform in environment.PlatformCandidates())
        {
            if (packages.FirstOrDefault(package => package.Text("platform") == platform) is { ValueKind: JsonValueKind.Object } found)
            {
                return (platform, found);
            }
        }

        throw new CliException(ExitCodes.Error, "這個伺服器沒有提供適合這台電腦的 kumi。");
    }

    private static async Task<int> ReportAsync(CommandCall call, string current, string latest, bool available, bool updated, string message)
    {
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, new JsonObject
            {
                ["current"] = current,
                ["latest"] = latest,
                ["updateAvailable"] = available,
                ["updated"] = updated,
                ["standalone"] = call.Context.Update.IsStandalone,
            });
        }
        else
        {
            await call.Out.WriteLineAsync(message);
        }

        return ExitCodes.Success;
    }
}
