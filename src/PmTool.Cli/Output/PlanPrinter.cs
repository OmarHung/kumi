using System.Text.Json;
using PmTool.Cli.Api;

namespace PmTool.Cli.Output;

/// <summary>藍圖計畫的文字輸出：差異、對既有資料的影響、擋住套用的原因。預覽與變更集詳情共用。</summary>
internal static class PlanPrinter
{
    private static readonly IReadOnlyDictionary<string, string> Resources = new Dictionary<string, string>
    {
        ["blueprint"] = "藍圖", ["metadata"] = "基本資料", ["field"] = "欄位", ["field_option"] = "選項",
        ["workflow"] = "流程", ["state"] = "狀態", ["transition"] = "轉換", ["item_type"] = "類型",
        ["view"] = "檢視", ["automation"] = "自動化", ["role"] = "角色", ["binding"] = "整合綁定",
    };

    public static long TotalAffected(JsonElement plan) => plan.List("impacts").Sum(impact => impact.Number("affected") ?? 0);

    /// <summary>有擋住的原因，或會動到既有資料：需要人決定。</summary>
    public static bool NeedsDecision(JsonElement plan) => plan.List("blockers").Count > 0 || TotalAffected(plan) > 0;

    public static async Task WriteChangesAndImpactsAsync(TextWriter output, JsonElement plan)
    {
        var changes = plan.List("changes");
        if (changes.Count == 0)
        {
            await output.WriteLineAsync("和目前的藍圖一樣，沒有差異。");
        }
        else
        {
            await output.WriteLineAsync($"差異（{changes.Count}）：");
            foreach (var change in changes)
            {
                await output.WriteLineAsync($"  {DescribeChange(change)}");
            }
        }

        var impacts = plan.List("impacts");
        if (impacts.Count > 0)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("對既有資料的影響：");
            foreach (var impact in impacts)
            {
                await output.WriteLineAsync($"  {DescribeImpact(impact)}");
            }
        }
    }

    public static async Task WriteBlockersAsync(TextWriter output, JsonElement plan)
    {
        var blockers = plan.List("blockers");
        if (blockers.Count == 0)
        {
            return;
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("現在還不能套用：");
        foreach (var blocker in blockers)
        {
            await output.WriteLineAsync($"  {blocker.Display("message")}");
        }
    }

    private static string DescribeChange(JsonElement change)
    {
        var mark = change.Text("kind") switch { "added" => "+ 新增", "removed" => "- 移除", _ => "~ 修改" };
        var resource = Resources.GetValueOrDefault(change.Text("resource") ?? string.Empty, change.Display("resource"));
        var parent = change.Text("parentKey") is { } key ? $"（{key} 底下）" : string.Empty;
        var changed = change.List("changedProperties");
        var properties = changed.Count > 0 ? $"：{change.Get("changedProperties").Display()}" : string.Empty;
        return $"{mark} {resource} {change.Display("key")}{parent}{properties}";
    }

    private static string DescribeImpact(JsonElement impact)
    {
        var count = impact.Number("affected") ?? 0;
        var text = impact.Text("kind") switch
        {
            "state_move" => $"{impact.Display("workflow")} 的狀態 {impact.Display("from")} 被拿掉，{count} 個項目會搬到 {impact.Display("to")}",
            "field_drop" => $"欄位 {impact.Display("field")} 被刪除，{count} 個項目的值會封存",
            "field_conversion" => $"欄位 {impact.Display("field")} 從 {impact.Display("fromType")} 轉成 {impact.Display("toType")}，{count} 個項目的值會轉換",
            "item_type_removal" => $"類型 {impact.Display("itemType")} 被刪除，底下還有 {count} 個項目",
            "parent_removal" => $"{impact.Display("itemType")} 不能再放在 {impact.Display("parent")} 底下，還有 {count} 個項目掛在那裡",
            "role_removal" => $"角色 {impact.Display("role")} 被刪除，還有 {count} 位成員是這個角色",
            "field_detach" => $"類型 {impact.Display("itemType")} 不再有欄位 {impact.Display("field")}，{count} 個項目的值會封存",
            var other => $"{other}：{count} 個項目",
        };
        return impact.Flag("blocksApply") ? $"{text}（擋住套用）" : text;
    }
}
