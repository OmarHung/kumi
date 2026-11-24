using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>已經匯入過的東西在伺服器上的位置（<c>POST /v1/history/lookup</c> 的一筆）。</summary>
internal sealed record ImportedRef(string ExternalId, string ProjectKey, string ItemKey, Guid TargetId);

/// <summary>伺服器回的一筆「建立」或「略過」。</summary>
internal sealed record ItemRef(string ExternalId, string ItemKey);

internal sealed record ItemsResult(IReadOnlyList<ItemRef> Created, IReadOnlyList<ItemRef> Skipped);

internal sealed record CommentsResult(int Created, int Skipped);

/// <summary>匯入用到的伺服器端點。請求與回應的形狀都在這裡，匯入的流程不碰 JSON。</summary>
internal sealed class HistoryApi(ApiClient api, string source, CancellationToken cancellationToken)
{
    private const int LookupChunk = 1_000;

    public async Task<IReadOnlyList<ServerPerson>> PeopleAsync() =>
        [.. (await api.GetAsync("people", null, cancellationToken)).Items().Select(ServerPerson.From).OfType<ServerPerson>()];

    public async Task<ProjectInfo> ProjectAsync(string key) =>
        ProjectInfo.From(key, await api.GetAsync($"projects/{Uri.EscapeDataString(key)}", null, cancellationToken));

    public async Task<IReadOnlyList<CycleInfo>> CyclesAsync(string projectKey) =>
        [.. (await api.GetAsync($"projects/{Uri.EscapeDataString(projectKey)}/cycles", null, cancellationToken)).Items()
            .Select(cycle => Guid.TryParse(cycle.Text("id"), out var id) && cycle.Text("name") is { } name ? new CycleInfo(id, name) : null)
            .OfType<CycleInfo>()];

    public async Task<CycleInfo> CreateCycleAsync(string projectKey, CycleMapping cycle)
    {
        var created = await api.PostAsync(
            $"projects/{Uri.EscapeDataString(projectKey)}/cycles",
            new JsonObject { ["name"] = cycle.Name, ["startDate"] = Date(cycle.Start), ["endDate"] = Date(cycle.End) },
            cancellationToken);
        return new CycleInfo(Guid.Parse(created.Text("id")!), created.Text("name") ?? cycle.Name);
    }

    public Task AddMemberAsync(string projectKey, Guid personId, string role) =>
        api.PutAsync($"projects/{Uri.EscapeDataString(projectKey)}/members/{personId}", new JsonObject { ["role"] = role }, cancellationToken);

    public async Task<ItemsResult> ImportItemsAsync(string projectKey, IEnumerable<JsonNode> rows, bool dryRun)
    {
        var data = await api.PostAsync(
            $"projects/{Uri.EscapeDataString(projectKey)}/history/items",
            new JsonObject { ["source"] = source, ["dryRun"] = dryRun, ["rows"] = new JsonArray([.. rows]) },
            cancellationToken);
        return new ItemsResult(Refs(data, "created"), Refs(data, "skipped"));
    }

    public async Task<CommentsResult> ImportCommentsAsync(string projectKey, IEnumerable<JsonNode> comments)
    {
        var data = await api.PostAsync(
            $"projects/{Uri.EscapeDataString(projectKey)}/history/comments",
            new JsonObject { ["source"] = source, ["comments"] = new JsonArray([.. comments]) },
            cancellationToken);
        return new CommentsResult(data.List("created").Count, data.List("skipped").Count);
    }

    /// <summary>
    /// 上傳附件。欄位的順序不能換（伺服器靠它在讀到檔案之前就認出已經匯入過的）：source、externalId、uploadedBy、uploadedAt，最後才是 file。
    /// </summary>
    public async Task<Guid> UploadAttachmentAsync(
        string itemKey, string externalId, Guid uploadedBy, DateTimeOffset uploadedAt, Stream content, string fileName, string contentType)
    {
        var created = await api.PostFormFileAsync(
            $"items/{Uri.EscapeDataString(itemKey)}/history/attachments",
            [("source", source), ("externalId", externalId), ("uploadedBy", uploadedBy.ToString()), ("uploadedAt", Moment(uploadedAt))],
            content, fileName, contentType, cancellationToken);
        return Guid.TryParse(created.Text("id"), out var id)
            ? id
            : throw new CliException(ExitCodes.Error, "伺服器沒有回附件的編號。");
    }

    /// <summary>查哪些外部編號已經匯入過。回傳外部編號 → 位置；沒匯入過的不在裡面。</summary>
    public async Task<IReadOnlyDictionary<string, ImportedRef>> LookupAsync(string kind, IEnumerable<string> externalIds)
    {
        var found = new Dictionary<string, ImportedRef>(StringComparer.Ordinal);
        foreach (var chunk in externalIds.Distinct(StringComparer.Ordinal).Chunk(LookupChunk))
        {
            var data = await api.PostAsync(
                "history/lookup",
                new JsonObject
                {
                    ["source"] = source,
                    ["kind"] = kind,
                    ["externalIds"] = new JsonArray([.. chunk.Select(id => (JsonNode?)JsonValue.Create(id))]),
                },
                cancellationToken);
            foreach (var reference in data.Items())
            {
                if (reference.Text("externalId") is { } externalId && Guid.TryParse(reference.Text("targetId"), out var target))
                {
                    found[externalId] = new ImportedRef(externalId, reference.Text("projectKey") ?? string.Empty, reference.Text("itemKey") ?? string.Empty, target);
                }
            }
        }

        return found;
    }

    public static string Moment(DateTimeOffset at) => at.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IReadOnlyList<ItemRef> Refs(JsonElement data, string name) =>
        [.. data.List(name).Select(item => new ItemRef(item.Text("externalId") ?? string.Empty, item.Text("itemKey") ?? string.Empty))];
}
