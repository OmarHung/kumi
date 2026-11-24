using PmTool.Abstractions;

namespace PmTool.Work.Tests;

internal static class ItemAssert
{
    private static readonly IReadOnlyDictionary<string, System.Text.Json.JsonElement> NoFields = new Dictionary<string, System.Text.Json.JsonElement>();

    /// <summary>規則擋下來的失敗：種類是 Invalid、代碼與 Details 的路徑都對，而且有給人看的訊息。</summary>
    public static void Invalid<T>(Result<T> result, string code, string path)
    {
        Assert.That(result.Failure, Is.Not.Null, "應該被擋下來");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure.Code, Is.EqualTo(code));
            Assert.That(result.Failure.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (code, path) }));
            Assert.That(result.Failure.Message, Is.Not.Empty);
        }
    }

    /// <summary>兩個項目的內容相同。自訂欄位的字典是兩份物件，比內容不比參考。</summary>
    public static void Same(WorkItem actual, WorkItem expected)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual with { Fields = NoFields }, Is.EqualTo(expected with { Fields = NoFields }));
            Assert.That(
                actual.Fields.ToDictionary(f => f.Key, f => f.Value.GetRawText()),
                Is.EqualTo(expected.Fields.ToDictionary(f => f.Key, f => f.Value.GetRawText())));
        }
    }

    public static void Failed<T>(Result<T> result, FailureKind kind, string code)
    {
        Assert.That(result.Failure, Is.Not.Null, "應該被擋下來");
        Assert.That((result.Failure!.Kind, result.Failure.Code), Is.EqualTo((kind, code)));
    }
}
