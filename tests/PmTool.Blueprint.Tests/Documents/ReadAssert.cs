using System.Text.Json.Nodes;
using PmTool.Blueprint.Documents;

namespace PmTool.Blueprint.Tests.Documents;

/// <summary>讀檔測試共用的斷言。讀檔階段的問題 Path 一律是空字串，所以每次都一起檢查。</summary>
internal static class ReadAssert
{
    public static void FailsWith(DocumentReadResult result, string code)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.Root, Is.Null);
            Assert.That(
                result.Issues.Select(issue => (issue.Code, issue.Path)),
                Is.EqualTo(new[] { (code, string.Empty) }));
        });
    }

    public static JsonNode? Succeeds(DocumentReadResult result)
    {
        Assert.That(result.Issues.Select(issue => issue.Code), Is.Empty);
        return result.Root;
    }

    public static JsonObject SucceedsWithObject(DocumentReadResult result)
    {
        var root = Succeeds(result);
        Assert.That(root, Is.InstanceOf<JsonObject>());
        return (JsonObject)root!;
    }
}
