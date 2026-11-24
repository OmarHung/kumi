using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Schema;

/// <summary>
/// structure.* 問題的訊息。文件裡的文字（屬性名稱、值）一律經過 <see cref="IssueText.Quote"/>；
/// Schema 裡的屬性名稱與允許的值是我們自己寫的，可以直接列出來，方便照著訊息修正。
/// </summary>
internal static class StructuralIssues
{
    public static ValidationIssue TypeMismatch(string path, SchemaType expected, JsonNode? actual) =>
        new(IssueCodes.StructureType, path, $"型別不符：應該是{ExpectedName(expected)}，實際是{InstanceTypes.Describe(actual)}。");

    public static ValidationIssue MissingRequired(string path, string propertyName) =>
        new(IssueCodes.StructureRequired, path, $"缺少必要的屬性{IssueText.Quote(propertyName)}。");

    public static ValidationIssue AdditionalProperty(string path, string propertyName, IReadOnlyList<string> allowed) =>
        new(
            IssueCodes.StructureAdditionalProperty,
            path,
            $"不允許的屬性{IssueText.Quote(propertyName)}。" + (allowed.Count == 0
                ? "這個物件不能有任何屬性。"
                : $"允許的屬性：{string.Join("、", allowed)}。"));

    public static ValidationIssue NotInEnum(string path, IReadOnlyList<ScalarValue> allowed, JsonNode? actual) =>
        new(
            IssueCodes.StructureEnum,
            path,
            $"值不在允許的值裡：{InstanceTypes.DescribeValue(actual)}。允許的值：{string.Join("、", allowed.Select(value => value.ToMessageText()))}。");

    public static ValidationIssue ConstMismatch(string path, ScalarValue expected, JsonNode? actual) =>
        new(
            IssueCodes.StructureConst,
            path,
            $"值不符。只能是：{expected.ToMessageText()}；實際是：{InstanceTypes.DescribeValue(actual)}。");

    private static string ExpectedName(SchemaType type) => type switch
    {
        SchemaType.Object => "物件",
        SchemaType.Array => "陣列",
        SchemaType.String => "字串",
        SchemaType.Integer => "整數（沒有小數部分，而且在 64 位元整數的範圍內）",
        SchemaType.Number => "數字",
        SchemaType.Boolean => "布林值",
        _ => type.ToString(),
    };
}
