using System.Text.Json;
using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>項目的文字輸出。</summary>
internal static class ItemPrinter
{
    /// <summary>詳情被截斷的地方：下層只列前幾個、活動只列最近幾筆時，要讓人知道還有。</summary>
    public readonly record struct Truncation(bool MoreChildren, int ChildrenShown, bool MoreActivity, int ActivityShown);

    public static async Task WriteTableAsync(TextWriter output, IEnumerable<JsonElement> items, BlueprintNames names)
    {
        var rows = items
            .Select(item => (IReadOnlyList<string>)
            [
                item.Display("key"), names.TypeName(item.Text("type")), item.Display("title"),
                names.StateName(item.Text("type"), item.Text("state")),
                item.Get("assignee")?.Display("displayName") ?? "-", item.Display("dueDate"),
            ])
            .ToArray();
        await TextTable.WriteAsync(output, ["編號", "類型", "標題", "狀態", "負責人", "到期"], rows);
    }

    public static async Task WriteDetailAsync(
        TextWriter output, JsonElement item, IReadOnlyList<JsonElement> children, IReadOnlyList<JsonElement> activity,
        BlueprintNames names, Truncation truncation)
    {
        var type = item.Text("type");
        await output.WriteLineAsync($"{item.Display("key")}  {item.Display("title")}");
        await output.WriteLineAsync(
            $"類型：{names.TypeName(type)}　狀態：{names.StateName(type, item.Text("state"))}（{Labels.StateCategory(item.Text("stateCategory"))}）");
        await output.WriteLineAsync(
            $"負責人：{item.Get("assignee")?.Display("displayName") ?? "-"}　優先順序：{Labels.Priority(item.Text("priority"))}　上層：{item.Display("parentKey")}");
        await output.WriteLineAsync(
            $"開始：{item.Display("startDate")}　到期：{item.Display("dueDate")}　估點：{item.Display("estimate")}");
        await output.WriteLineAsync($"建立：{JsonAccess.Moment(item.Text("createdAt"))}　更新：{JsonAccess.Moment(item.Text("updatedAt"))}");

        if (item.Get("fields") is { ValueKind: JsonValueKind.Object } fields && fields.EnumerateObject().Any())
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("欄位：");
            foreach (var field in fields.EnumerateObject())
            {
                await output.WriteLineAsync($"  {names.FieldName(field.Name)}：{field.Value.Display()}");
            }
        }

        if (item.Text("description") is { Length: > 0 } description)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("說明：");
            foreach (var line in description.Split('\n'))
            {
                await output.WriteLineAsync($"  {line.TrimEnd('\r')}");
            }
        }

        await WriteChildrenAsync(output, children, names, truncation);
        await WriteActivityAsync(output, activity, type, names, truncation);
    }

    private static async Task WriteChildrenAsync(
        TextWriter output, IReadOnlyList<JsonElement> children, BlueprintNames names, Truncation truncation)
    {
        if (children.Count == 0)
        {
            return;
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync($"下層（{children.Count}）：");
        foreach (var child in children)
        {
            await output.WriteLineAsync(
                $"  {child.Display("key")}  {child.Display("title")}［{names.StateName(child.Text("type"), child.Text("state"))}］");
        }

        if (truncation.MoreChildren)
        {
            await output.WriteLineAsync($"  …還有更多下層，這裡只列出前 {truncation.ChildrenShown} 個。");
        }
    }

    private static async Task WriteActivityAsync(
        TextWriter output, IReadOnlyList<JsonElement> activity, string? type, BlueprintNames names, Truncation truncation)
    {
        if (activity.Count == 0)
        {
            return;
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("最近的活動：");
        foreach (var entry in activity)
        {
            var who = entry.Get("actor")?.Display("displayName") ?? "-";
            await output.WriteLineAsync($"  {JsonAccess.Moment(entry.Text("at"))}  {who}  {Describe(entry, type, names)}");
        }

        if (truncation.MoreActivity)
        {
            await output.WriteLineAsync($"  …還有更早的活動，這裡只列出最近 {truncation.ActivityShown} 筆。");
        }
    }

    private static string Describe(JsonElement entry, string? type, BlueprintNames names) => entry.Text("kind") switch
    {
        "created" => "建立了項目",
        "commented" when entry.Text("deletedAt") is not null => "留言已刪除",
        "commented" => $"留言{(entry.Text("editedAt") is null ? string.Empty : "（已編輯）")}：{FirstLine(entry.Text("comment"))}",
        "transitioned" => $"狀態 {names.StateName(type, entry.Text("fromState"))} → {names.StateName(type, entry.Text("toState"))}",
        "deleted" => "刪除了項目",
        "migrated" => "藍圖套用時搬移了資料",
        var kind => $"{(kind == "type_changed" ? "改了類型" : "修改")}：{ChangedFields(entry)}",
    };

    private static string ChangedFields(JsonElement entry) =>
        string.Join("、", entry.List("changes").Select(change => change.Display("field")));

    private static string FirstLine(string? text) =>
        (text ?? string.Empty).Split('\n')[0].TrimEnd('\r');
}
