using System.Text.Json;
using PmTool.Blueprint;
using PmTool.Blueprint.Planning;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 變更集在 API 上的形狀。另外定義而不直接輸出合約的型別，模組加屬性時對外的回應才不會跟著變。
/// 提出與決定的人帶顯示名稱，網頁不必另外查；沒有的屬性寫成 null，不省略。
/// </summary>
/// <param name="Origin">從哪裡提出的：<c>direct</c>（人自己送的）、<c>assistant</c>（AI 助理代提出的）或 <c>agent</c>（使用者自己的 AI 代理透過 MCP 送出）；核准的人要知道內容是模型寫的。</param>
internal sealed record ChangeSetData(
    long Number,
    ChangeSetStatus Status,
    long BaseVersion,
    long? AppliedVersion,
    string? Summary,
    PersonReference ProposedBy,
    DateTimeOffset ProposedAt,
    PersonReference? DecidedBy,
    DateTimeOffset? DecidedAt,
    ChangeSetOrigin Origin)
{
    public static ChangeSetData From(ChangeSetSummary changeSet, PeopleNames names) => new(
        changeSet.Number,
        changeSet.Status,
        changeSet.BaseVersion,
        changeSet.AppliedVersion,
        changeSet.Summary,
        new PersonReference(changeSet.ProposedBy, names.NameOf(changeSet.ProposedBy)),
        changeSet.ProposedAt,
        changeSet.DecidedBy is { } decidedBy ? new PersonReference(decidedBy, names.NameOf(decidedBy)) : null,
        changeSet.DecidedAt,
        changeSet.Origin);

    /// <summary>清單裡所有提出與決定的人一次查完。</summary>
    public static async Task<IReadOnlyList<ChangeSetData>> FromManyAsync(
        IReadOnlyList<ChangeSetSummary> changeSets, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        var names = await PeopleNames.LookupAsync(people, organizationId, changeSets.SelectMany(PeopleOf), cancellationToken);
        return [.. changeSets.Select(changeSet => From(changeSet, names))];
    }

    public static IEnumerable<Guid> PeopleOf(ChangeSetSummary changeSet) =>
        changeSet.DecidedBy is { } decidedBy ? [changeSet.ProposedBy, decidedBy] : [changeSet.ProposedBy];
}

/// <summary>相對於基準版本的一項差異。目前與新藍圖裡的位置是計畫內部用的，不放進來。</summary>
internal sealed record ChangeData(
    ChangeKind Kind, ResourceKind Resource, string Key, string? ParentKey, IReadOnlyList<string> ChangedProperties)
{
    public static ChangeData From(BlueprintChange change) =>
        new(change.Kind, change.Resource, change.Key, change.ParentKey, change.ChangedProperties);
}

/// <summary>現在套用不了的一個原因，形狀和錯誤信封裡的細項一樣。</summary>
internal sealed record BlockerData(string Code, string Path, string Message);

/// <param name="Blueprint">變更集想要的藍圖全文，和專案詳情的 blueprint 同一個形狀。</param>
/// <param name="BaseBlueprint">
/// 基準版本的藍圖全文，差異的「改之前」。已套用的變更集不能拿專案目前的藍圖來比，那已經是改之後的樣子。
/// </param>
internal sealed record ChangeSetDetailData(
    ChangeSetData ChangeSet,
    JsonElement Blueprint,
    JsonElement BaseBlueprint,
    IReadOnlyList<ChangeData> Changes,
    IReadOnlyList<ImpactData> Impacts,
    IReadOnlyList<BlockerData> Blockers,
    bool CanApply)
{
    public static async Task<ChangeSetDetailData> FromAsync(
        ChangeSetDetail detail, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        var names = await PeopleNames.LookupAsync(
            people, organizationId, ChangeSetData.PeopleOf(detail.ChangeSet), cancellationToken);
        return new ChangeSetDetailData(
            ChangeSetData.From(detail.ChangeSet, names),
            BlueprintSerializer.ToElement(detail.Blueprint),
            BlueprintSerializer.ToElement(detail.BaseBlueprint),
            [.. detail.Changes.Select(ChangeData.From)],
            [.. detail.Impacts.Select(ImpactData.FromPlanned)],
            [.. detail.Blockers.Select(blocker => new BlockerData(blocker.Code, blocker.Path, blocker.Message))],
            detail.CanApply);
    }
}

/// <summary>預覽的回應：沒有存下來，所以沒有編號與提出的人；形狀其他部分和變更集詳情一樣。</summary>
internal sealed record ChangeSetPreviewData(
    long BaseVersion,
    long CurrentVersion,
    JsonElement Blueprint,
    IReadOnlyList<ChangeData> Changes,
    IReadOnlyList<ImpactData> Impacts,
    IReadOnlyList<BlockerData> Blockers,
    bool CanApply)
{
    public static ChangeSetPreviewData From(ChangeSetPreview preview) => new(
        preview.BaseVersion,
        preview.CurrentVersion,
        BlueprintSerializer.ToElement(preview.Blueprint),
        [.. preview.Changes.Select(ChangeData.From)],
        [.. preview.Impacts.Select(ImpactData.FromPlanned)],
        [.. preview.Blockers.Select(blocker => new BlockerData(blocker.Code, blocker.Path, blocker.Message))],
        preview.CanApply);
}
