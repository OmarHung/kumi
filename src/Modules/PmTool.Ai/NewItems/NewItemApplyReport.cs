namespace PmTool.Ai.NewItems;

/// <summary>套用之後「哪些沒建立」的說明文字（給人看，繁體中文）。</summary>
internal static class NewItemApplyReport
{
    /// <summary>有項目沒建立成功：<paramref name="reasons"/> 是依項目順序的「第 N 個」與原因。</summary>
    public static string PartlyFailed(IReadOnlyList<NewItemDraft> items, IReadOnlyList<(int Index, string Reason)> reasons) =>
        "有幾個沒有建立：" + string.Join("；", reasons.Select(r => $"第 {r.Index + 1} 個「{items[r.Index].Title}」（{r.Reason}）")) + "。其餘已經建立。";

    public static string Interrupted(IReadOnlyList<NewItemDraft> items, IReadOnlyList<string?> keys)
    {
        var missing = Enumerable.Range(0, items.Count).Where(i => i >= keys.Count || keys[i] is null).ToList();
        return missing.Count == 0
            ? "建立的過程中斷了。"
            : "建立的過程中斷了。這幾個沒有建立：" + string.Join("、", missing.Select(i => $"第 {i + 1} 個「{items[i].Title}」")) + "。其餘已經建立。";
    }
}
