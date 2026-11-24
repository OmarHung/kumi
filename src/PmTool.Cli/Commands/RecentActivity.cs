using System.Text.Json;
using PmTool.Cli.Api;

namespace PmTool.Cli.Commands;

/// <summary>
/// 項目最近的幾筆活動。伺服器的活動由舊到新、一頁一頁往後接，第一頁是最舊的；要最近的就得照游標讀到最後一頁。
/// 原本只讀第一頁，活動超過 10 筆的項目「最近的活動」其實是最早的 10 筆。
/// </summary>
internal static class RecentActivity
{
    /// <summary>伺服器一頁最多 200 筆（ActivityQuery.MaxLimit）。</summary>
    private const int PageSize = 200;

    /// <summary>最多讀這麼多頁（一萬筆）：伺服器出錯一直給游標時不會無限讀下去。</summary>
    private const int MaxPages = 50;

    /// <param name="Newest">最近的幾筆，新的在前。</param>
    /// <param name="HasEarlier">還有更早的（沒列出來的）。</param>
    /// <param name="ReachedPageLimit">讀到上限就停了：列出的不一定是最新的。</param>
    public sealed record Result(IReadOnlyList<JsonElement> Newest, bool HasEarlier, bool ReachedPageLimit);

    public static async Task<Result> LoadAsync(CommandCall call, string escapedKey, int count)
    {
        var kept = new Queue<JsonElement>(count + 1);
        var seen = 0;
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var query = new List<KeyValuePair<string, string>> { new("limit", $"{PageSize}") };
            if (cursor is not null)
            {
                query.Add(new("cursor", cursor));
            }

            var response = await call.Api.GetAsync($"items/{escapedKey}/activity", query, call.CancellationToken);
            foreach (var entry in response.List("items"))
            {
                seen++;
                kept.Enqueue(entry.Clone());
                if (kept.Count > count)
                {
                    kept.Dequeue();
                }
            }

            cursor = response.Text("nextCursor");
            if (cursor is null)
            {
                return new Result([.. kept.Reverse()], seen > count, ReachedPageLimit: false);
            }
        }

        return new Result([.. kept.Reverse()], HasEarlier: true, ReachedPageLimit: true);
    }
}
