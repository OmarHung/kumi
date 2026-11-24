using System.Globalization;
using PmTool.Abstractions;
using PmTool.Knowledge;

namespace PmTool.Api.Endpoints;

internal sealed record NewKnowledgeSpaceRequest(string Key, string Name, string? Description = null, string? ProjectKey = null)
{
    public NewSpace ToNew() => new(Key, Name, Description, ProjectKey);
}

internal sealed record KnowledgeSpaceSettingsRequest(string Name, string? Description = null)
{
    public SpaceSettings ToSettings() => new(Name, Description);
}

/// <summary>沒送 parentNumber 就是最上層（和送 null 一樣）：只有標題是必填。</summary>
internal sealed record NewKnowledgePageRequest
{
    public int? ParentNumber { get; init; }

    public required string Title { get; init; }

    public string? Body { get; init; }

    public int? BeforeNumber { get; init; }

    public NewPage ToNew() => new(ParentNumber, Title, Body) { BeforeNumber = BeforeNumber };
}

/// <summary>JSON 的 null 和沒送一樣是不改（標題不能清空，內文清空送空字串）。</summary>
internal sealed record PatchKnowledgePageRequest(string? Title = null, string? Body = null, long? ExpectedRevision = null);

internal sealed record KnowledgePageMoveRequest(int? ParentNumber = null, int? BeforeNumber = null)
{
    public PageMove ToMove() => new(ParentNumber, BeforeNumber);
}

internal sealed record KnowledgeRestrictionRequest(IReadOnlyList<Guid>? People = null);

/// <summary>端點共用的小工具：路徑參數的解析與知識庫自己的 API 層失敗。</summary>
internal static class KnowledgeRoutes
{
    private const string RevisionRequiredCode = KnowledgeErrorCodes.PageRevisionRequired;

    /// <summary>不是正整數的頁面編號和找不到頁面同一個回答：不讓人從回應分辨格式錯誤與不存在。</summary>
    public static bool TryParseNumber(string number, out int value) =>
        int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;

    public static Failure PageNotFound(string number) => new(
        FailureKind.NotFound, KnowledgeErrorCodes.PageNotFound, $"找不到第 {Shorten(number)} 頁。");

    public static Failure AttachmentNotFound() => new(
        FailureKind.NotFound, KnowledgeErrorCodes.AttachmentNotFound, "找不到這個附件。");

    public static Failure RevisionRequired() => new(
        FailureKind.Invalid, RevisionRequiredCode, "修改頁面要帶 expectedRevision（開始編輯時拿到的 revision）。")
    {
        Details = [new FailureDetail(RevisionRequiredCode, "expectedRevision", "修改頁面要帶 expectedRevision（開始編輯時拿到的 revision）。")],
    };

    private static string Shorten(string text) => text.Length <= 32 ? text : text[..32] + "…";
}
