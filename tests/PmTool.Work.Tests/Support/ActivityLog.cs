using System.Text.Encodings.Web;
using System.Text.Json;
using PmTool.Abstractions;

namespace PmTool.Work.Tests;

/// <summary>透過公開介面讀項目的活動，並把欄位的前後值寫成好比對的文字。</summary>
internal static class ActivityLog
{
    // 中文不跳脫，預期值才能直接寫中文。
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>一頁一頁讀到底，全部的活動由舊到新。</summary>
    public static async Task<IReadOnlyList<ItemActivity>> AllAsync(WorkTestHost host, Actor actor, string itemKey)
    {
        var all = new List<ItemActivity>();
        string? cursor = null;
        do
        {
            var page = await host.Activity.ListAsync(
                actor, itemKey, new ActivityQuery { Limit = ActivityQuery.MaxLimit, Cursor = cursor }, CancellationToken.None);
            Assert.That(page.Succeeded, Is.True, () => page.Failure!.Message);
            all.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        return all;
    }

    public static async Task<ItemActivity> LastAsync(WorkTestHost host, Actor actor, string itemKey) =>
        (await AllAsync(host, actor, itemKey))[^1];

    /// <summary>每個欄位的名字與前後值；值寫成不帶空白的 JSON，沒有值是 null。</summary>
    public static (string Field, string? From, string? To)[] Shapes(ItemActivity activity) =>
        [.. activity.Changes.Select(c => (c.Field, Text(c.From), Text(c.To)))];

    public static string? Text(JsonElement? value) => value is { } element ? JsonSerializer.Serialize(element, Compact) : null;
}
