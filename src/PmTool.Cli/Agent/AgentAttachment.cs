using System.Text.Json;
using PmTool.Cli.Api;

namespace PmTool.Cli.Agent;

/// <summary>附件的種類，字串就是伺服器 <c>AssistantAttachmentKind</c> 的 JSON 寫法。</summary>
internal static class AttachmentKinds
{
    public const string Image = "image";
    public const string Pdf = "pdf";
}

/// <summary>
/// 工作帶來的一個要交給引擎的圖片或 PDF（<c>attachments</c> 的一筆）。檔名只用來顯示：落地的檔名一律由 <see cref="LocalFileName"/> 決定。
/// 欄位缺漏或型別不對時不丟例外、留成不合格的值，由 <see cref="JobValidation"/> 擋下並說明，工作不能默默少一張圖。
/// </summary>
internal sealed record AgentAttachment(Guid Id, string FileName, string ContentType, string Kind, long SizeBytes, string Sha256)
{
    /// <summary>每種內容類型對應的副檔名；只收伺服器認得、兩個引擎都讀得了的格式。</summary>
    private static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["image/png"] = "png",
        ["image/jpeg"] = "jpg",
        ["image/gif"] = "gif",
        ["image/webp"] = "webp",
        ["application/pdf"] = "pdf",
    };

    public static AgentAttachment Parse(JsonElement element) => new(
        Guid.TryParse(element.Text("id"), out var id) ? id : Guid.Empty,
        element.Text("fileName") ?? string.Empty,
        element.Text("contentType") ?? string.Empty,
        element.Text("kind") ?? string.Empty,
        element.Number("sizeBytes") ?? -1,
        element.Text("sha256") ?? string.Empty);

    /// <summary>內容類型對得上的副檔名；不認得回 null。</summary>
    public static string? ExtensionOf(string contentType) => Extensions.GetValueOrDefault(contentType);

    /// <summary>落地的檔名：<c>attachment-N.副檔名</c>（N 從 1 起算）。不含任何伺服器給的字。</summary>
    public string LocalFileName(int number) => $"attachment-{number}.{ExtensionOf(ContentType)}";
}

/// <summary>已經下載到工作暫存資料夾、核對過的附件，給 <see cref="IEngineSpec.Plan"/> 組命令列與標準輸入。</summary>
internal sealed record StagedAttachment(string Path, string ContentType, string Kind);
