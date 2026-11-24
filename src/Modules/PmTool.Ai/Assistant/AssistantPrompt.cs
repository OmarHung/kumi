using System.Reflection;
using System.Text;
using PmTool.Work;

namespace PmTool.Ai.Assistant;

/// <summary>
/// 系統提示：自己寫的規則加上三份參考資料。整份在程序裡組一次、之後不變，Anthropic 的提示快取才有效
/// （所以不放日期、專案名稱這類會變的內容；專案與權限的說明是另一則 system 訊息）。
/// </summary>
internal static class AssistantPrompt
{
    private const string ResourcePrefix = "PmTool.Ai.Prompts.";

    private static readonly string[] References =
        ["project-blueprint.schema.json", "changeset.schema.json", "planning-structure.md"];

    private static readonly Lazy<string> Composed = new(Compose, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string SystemPrompt => Composed.Value;

    /// <summary>這則對話的專案與使用者權限的一句說明，放在系統提示之後。</summary>
    public static string ProjectNote(string projectName, string projectKey, IReadOnlyList<string> permissions) =>
        $"這則對話在專案「{projectName}」（代號 {projectKey}）。使用者在這個專案：能讀設定；"
        + $"{(permissions.Contains(WorkPermissions.BlueprintPropose) ? "能" : "不能")}提出變更集；"
        + $"{(permissions.Contains(WorkPermissions.BlueprintApply) ? "能" : "不能")}核准。";

    /// <summary>
    /// 使用者選了項目時的一句說明。只放編號：標題是使用者寫的內容，放進系統訊息等於讓它有系統提示的份量；
    /// 要知道標題與內容就用 get_item 讀（工具的結果有標明是使用者寫的）。
    /// </summary>
    public static string ItemNote(string itemKey) => $"使用者正在看項目 {itemKey}，用 get_item 讀它。";

    private static string Compose()
    {
        var builder = new StringBuilder(ReadResource("system.md").TrimEnd());
        foreach (var name in References)
        {
            builder.Append("\n\n<reference name=\"").Append(name).Append("\">\n")
                .Append(ReadResource(name).Trim())
                .Append("\n</reference>");
        }

        return builder.ToString();
    }

    internal static string ReadResource(string name)
    {
        var assembly = typeof(AssistantPrompt).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new InvalidOperationException($"組件裡找不到內嵌的提示：{name}。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
