using System.Text.Json;

namespace PmTool.Ai.Actions.Attachments;

/// <summary>
/// 一步附件動作的整理結果，原樣存進建議的 State。attach 的 <see cref="AttachmentId"/> 是這則對話裡的助理附件（套用時再讀一次檔案）；
/// delete 的是項目上的附件編號。
/// </summary>
internal sealed record AttachmentStep
{
    public const string Attach = "attach";
    public const string Delete = "delete";

    public required string Action { get; init; }

    public required Guid AttachmentId { get; init; }

    public required string FileName { get; init; }

    public string? ItemKey { get; init; }

    /// <summary>項目所在的專案（網址與權限用）。</summary>
    public string? ProjectKey { get; init; }

    public string? SpaceKey { get; init; }

    public int? PageNumber { get; init; }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static JsonElement Write(IReadOnlyList<AttachmentStep> steps) =>
        JsonSerializer.SerializeToElement(new { steps }, Options);

    public static AttachmentStep Read(JsonElement state, int index) =>
        state.GetProperty("steps")[index].Deserialize<AttachmentStep>(Options)
        ?? throw new InvalidOperationException("附件建議的狀態壞了。");
}
