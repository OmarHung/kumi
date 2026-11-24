using PmTool.Abstractions;

namespace PmTool.Work.Tests.History;

/// <summary>歷史匯入測試用的小工具：造請求、把失敗的路徑整理成好比對的樣子。</summary>
internal static class HistorySupport
{
    public const string Source = "monday";

    public static DateTimeOffset Past(int days = 30) => WorkTestHost.StartTime.AddDays(-days);

    public static HistoryItem Row(
        string externalId, Guid createdBy, string title = "舊項目", DateTimeOffset? createdAt = null, string? state = null, string? parent = null) => new()
        {
            ExternalId = externalId,
            Item = new NewWorkItem { Type = "task", Title = title },
            State = state,
            ParentExternalId = parent,
            CreatedBy = createdBy,
            CreatedAt = createdAt ?? Past(),
        };

    /// <summary>換成別的類型（agency_project 的需求與任務有上下層）。</summary>
    public static HistoryItem OfType(this HistoryItem row, string type) => row with { Item = row.Item with { Type = type } };

    public static HistoryItems Batch(bool dryRun = false, params HistoryItem[] rows) => new() { Source = Source, Rows = rows, DryRun = dryRun };

    public static HistoryItems Batch(params HistoryItem[] rows) => Batch(false, rows);

    public static HistoryComment Comment(
        string externalId, string itemExternalId, Guid authorId, string body = "舊留言", DateTimeOffset? at = null, string? replyTo = null) => new()
        {
            ExternalId = externalId,
            ItemExternalId = itemExternalId,
            AuthorId = authorId,
            At = at ?? Past(20),
            Body = body,
            ReplyToExternalId = replyTo,
        };

    public static HistoryComments Comments(params HistoryComment[] comments) => new() { Source = Source, Comments = comments };

    public static HistoryAttachment Attachment(
        string externalId, Guid uploadedBy, string fileName = "截圖.png", string content = "內容", DateTimeOffset? at = null, Stream? stream = null) => new()
        {
            Source = Source,
            ExternalId = externalId,
            FileName = fileName,
            ContentType = "image/png",
            Content = stream ?? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
            UploadedBy = uploadedBy,
            UploadedAt = at ?? Past(10),
        };

    public static string[] Paths(Failure? failure) => [.. failure?.Details.Select(d => d.Path) ?? []];

    /// <summary>一個專案管理員（不是組織管理員），和他加進專案的成員。</summary>
    public static async Task<(Scenario Scenario, Actor Manager, Actor Member)> ProjectWithMembersAsync(
        WorkTestHost host, string key = "WEB", string template = Scenario.SimpleTasks)
    {
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync(key, template);
        var manager = await scenario.MemberAsync(key, "admin");
        var member = await scenario.MemberAsync(key, "editor");
        return (scenario, manager, member);
    }
}
