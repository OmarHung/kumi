using System.Text.Json;
using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>已經送出的變更集：看清單與內容、核准套用、拒絕。</summary>
internal static class ChangeSetCommands
{
    private static readonly OptionSpec Project = new("project", "代號", "專案代號", 'p');

    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("changeset", "list", "列出專案的變更集", "kumi changeset list --project 代號", [Project], 0, 0, ListAsync),
        new("changeset", "show", "看一份變更集的差異與影響", "kumi changeset show 編號 --project 代號", [Project], 1, 1, ShowAsync),
        new("changeset", "apply", "核准並套用一份等待核准的變更集", "kumi changeset apply 編號 --project 代號", [Project], 1, 1, ApplyAsync),
        new("changeset", "reject", "拒絕一份等待核准的變更集", "kumi changeset reject 編號 --project 代號", [Project], 1, 1, RejectAsync),
    ];

    private static async Task<int> ListAsync(CommandCall call)
    {
        var changeSets = await call.Api.GetAsync(Path(call), null, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, changeSets);
            return ExitCodes.Success;
        }

        var rows = changeSets.Items()
            .Select(changeSet => (IReadOnlyList<string>)
            [
                $"#{changeSet.Display("number")}", StatusName(changeSet.Text("status")), changeSet.Display("baseVersion"),
                changeSet.Display("appliedVersion"), changeSet.Display("summary"),
                changeSet.Get("proposedBy")?.Display("displayName") ?? "-", JsonAccess.Moment(changeSet.Text("proposedAt")),
            ])
            .ToArray();
        await TextTable.WriteAsync(call.Out, ["編號", "狀態", "基準版本", "套用後版本", "說明", "提出的人", "提出時間"], rows);
        return ExitCodes.Success;
    }

    private static async Task<int> ShowAsync(CommandCall call)
    {
        var detail = await call.Api.GetAsync(ItemPath(call), null, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, detail);
            return ExitCodes.Success;
        }

        var changeSet = detail.Get("changeSet") ?? default;
        await call.Out.WriteLineAsync($"變更集 #{changeSet.Display("number")}：{StatusName(changeSet.Text("status"))}");
        await call.Out.WriteLineAsync($"說明：{changeSet.Display("summary")}");
        await call.Out.WriteLineAsync(
            $"提出：{changeSet.Get("proposedBy")?.Display("displayName") ?? "-"}（{JsonAccess.Moment(changeSet.Text("proposedAt"))}）　基準版本：{changeSet.Display("baseVersion")}");
        await call.Out.WriteLineAsync();
        await PlanPrinter.WriteChangesAndImpactsAsync(call.Out, detail);
        await PlanPrinter.WriteBlockersAsync(call.Out, detail);
        return ExitCodes.Success;
    }

    private static async Task<int> ApplyAsync(CommandCall call)
    {
        var number = Number(call);
        var detail = await call.Api.PostAsync($"{Path(call)}/{number}/apply", null, call.CancellationToken);
        return await ReportAsync(call, detail, $"已套用 #{number}，專案的藍圖現在是第 {detail.Get("changeSet")?.Display("appliedVersion") ?? "-"} 版。");
    }

    private static async Task<int> RejectAsync(CommandCall call)
    {
        var number = Number(call);
        var detail = await call.Api.PostAsync($"{Path(call)}/{number}/reject", null, call.CancellationToken);
        return await ReportAsync(call, detail, $"已拒絕 #{number}。");
    }

    private static async Task<int> ReportAsync(CommandCall call, JsonElement detail, string message)
    {
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, detail);
        }
        else
        {
            await call.Out.WriteLineAsync(message);
        }

        return ExitCodes.Success;
    }

    private static string Path(CommandCall call) => $"projects/{Uri.EscapeDataString(call.Arguments.Required("project"))}/changesets";

    private static string ItemPath(CommandCall call) => $"{Path(call)}/{Number(call)}";

    /// <summary>編號先在這裡確認是正整數（寫成 #5 也行）：不是的話伺服器只會回一個找不到的 404，看不出是打錯了。</summary>
    private static long Number(CommandCall call)
    {
        var text = call.Arguments.Positionals[0].TrimStart('#');
        return long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw CliException.Usage($"變更集的編號要是正整數，收到：{call.Arguments.Positionals[0]}。");
    }

    private static string StatusName(string? status) => status switch
    {
        "pending" => "等待核准",
        "applied" => "已套用",
        "rejected" => "已拒絕",
        "expired" => "已過期",
        var other => other ?? "-",
    };
}
