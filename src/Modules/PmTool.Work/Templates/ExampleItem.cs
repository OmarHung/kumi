using System.Text.Json;
using PmTool.Work.Blueprints;
using PmTool.Work.Items;

namespace PmTool.Work.Templates;

/// <summary>
/// 範例檔裡的一筆項目，已經通過讀取與驗證。日期是相對建立當天的天數，建立專案時才換成真正的日期。
/// </summary>
/// <param name="Position">第幾筆，從 1 開始；錯誤訊息用它指出是哪一筆。</param>
/// <param name="Ref">只給同一個檔案裡的上層參照用，不存進資料庫。</param>
/// <param name="State">null 表示類型的流程的第一個狀態。</param>
/// <param name="Parent">前面出現過的 <paramref name="Ref"/>。</param>
/// <param name="Fields">自訂欄位原本的值；日期欄位是相對天數。</param>
/// <param name="Cycle">同一個檔案的週期的 ref。</param>
/// <param name="Milestone">同一個檔案的里程碑的 ref。</param>
internal sealed record ExampleItem(
    int Position,
    string? Ref,
    string Type,
    string Title,
    string? State,
    Priority Priority,
    string? Description,
    string? Parent,
    int? StartInDays,
    int? DueInDays,
    IReadOnlyDictionary<string, JsonElement> Fields,
    decimal? Estimate = null,
    string? Cycle = null,
    string? Milestone = null)
{
    /// <summary>換成真正要存的值：日期算出來、文字去掉頭尾空白、數字正規化，和一般新增項目存的一樣。只能對驗證過的範例呼叫。</summary>
    public ResolvedExample Resolve(ProjectSchema schema, DateOnly today)
    {
        var type = schema.ItemTypes[Type];
        var fields = ExampleFields.Check(schema, type, Fields, today);
        if (fields.Problems.Count > 0)
        {
            throw new InvalidOperationException($"範例第 {Position} 筆的欄位沒有通過驗證，不能用來建立項目。");
        }

        return new ResolvedExample(
            CoreFieldRules.Title(Title).Value.Value,
            CoreFieldRules.Description(Description).Value.Value,
            StartInDays is { } start ? today.AddDays(start) : null,
            DueInDays is { } due ? today.AddDays(due) : null,
            fields.Values,
            CoreFieldRules.Estimate(Estimate).Value.Value);
    }
}

internal sealed record ResolvedExample(
    string Title,
    string? Description,
    DateOnly? StartDate,
    DateOnly? DueDate,
    IReadOnlyDictionary<string, JsonElement> Fields,
    decimal? Estimate);
