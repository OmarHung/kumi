using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.History;

/// <summary>
/// 歷史匯入（<see cref="IHistoryImportService"/>）。每一批在同一個交易裡做完：全部或全部不；
/// 規則重用新增與試算表匯入的（項目）、留言的（<c>CommentRules</c>）、附件的（<c>AttachmentNames</c>）。
/// 項目在這個檔案，留言、附件、查詢各一個檔案。
/// </summary>
internal sealed partial class HistoryImportService(
    IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache, Attachments.IAttachmentStore store, TimeProvider time, ILogger<HistoryImportService> logger)
    : IHistoryImportService
{
    public async Task<Result<HistoryItemsResult>> ImportItemsAsync(
        Actor actor, string projectKey, HistoryItems import, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(import);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var opened = await HistoryRefs.OpenForWriteAsync(db, cache, actor, projectKey, import.Source, cancellationToken);
        if (opened.Failure is { } denied)
        {
            return denied;
        }

        if (CheckItemsRequest(import) is { } invalidRequest)
        {
            return invalidRequest;
        }

        var importer = new HistoryItemsImporter(db, opened.Value, actor, import, await VisibleProjectsAsync(db, actor, opened.Value, cancellationToken), time);
        await importer.RunAsync(cancellationToken);
        if (importer.Problems.Count > 0)
        {
            return ItemImportRules.ToFailure(importer.Problems);
        }

        if (import.DryRun)
        {
            return new HistoryItemsResult(DryRun: true, [], importer.Skipped);
        }

        await transaction.CommitAsync(cancellationToken);
        return new HistoryItemsResult(DryRun: false, importer.Created, importer.Skipped);
    }

    /// <summary>看得到的專案，加上正在寫的這一個（能寫就一定看得到它）。已經匯入到別處的外部編號，只有在看得到的專案才能透露那邊的編號。</summary>
    private async Task<IReadOnlySet<Guid>> VisibleProjectsAsync(
        WorkDbContext db, Actor actor, ProjectAccess access, CancellationToken cancellationToken)
    {
        var readable = await ReadableProjects.IdsAsync(db, cache, actor, cancellationToken);
        return new HashSet<Guid>(readable) { access.Project.Id };
    }

    private static Failure? CheckItemsRequest(HistoryItems import)
    {
        if (HistoryRules.CheckSource(import.Source) is { } source)
        {
            return source;
        }

        return import.Rows is null or { Count: 0 } || import.Rows.Count > HistoryItems.MaxRows
            ? HistoryRules.CountProblem("rows", HistoryItems.MaxRows, import.Rows?.Count ?? 0)
            : null;
    }

    public async Task<Result<HistoryCommentsResult>> ImportCommentsAsync(
        Actor actor, string projectKey, HistoryComments import, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(import);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var opened = await HistoryRefs.OpenForWriteAsync(db, cache, actor, projectKey, import.Source, cancellationToken);
        if (opened.Failure is { } denied)
        {
            return denied;
        }

        if (CheckCommentsRequest(import) is { } invalidRequest)
        {
            return invalidRequest;
        }

        var importer = new HistoryCommentsImporter(db, opened.Value, actor, import, await VisibleProjectsAsync(db, actor, opened.Value, cancellationToken), time);
        await importer.RunAsync(cancellationToken);
        if (importer.Problems.Count > 0)
        {
            return ItemImportRules.ToFailure(importer.Problems, "留言");
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HistoryCommentsResult(importer.Created, importer.Skipped);
    }

    private static Failure? CheckCommentsRequest(HistoryComments import)
    {
        if (HistoryRules.CheckSource(import.Source) is { } source)
        {
            return source;
        }

        return import.Comments is null or { Count: 0 } || import.Comments.Count > HistoryComments.MaxComments
            ? HistoryRules.CountProblem("comments", HistoryComments.MaxComments, import.Comments?.Count ?? 0)
            : null;
    }
}
