using System.Text.Json.Nodes;
using PmTool.Cli.Commands;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>跨階段共用的狀態：哪些項目已經在伺服器上、在哪裡。</summary>
internal sealed class ImportState
{
    /// <summary>外部編號 → 項目編號：這個專案裡已經有的（之前匯入的，或這一次建立的）。</summary>
    public Dictionary<string, string> ItemKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>這一次開始之前就匯入過、在這個專案裡的項目。</summary>
    public HashSet<string> PreExisting { get; } = new(StringComparer.Ordinal);

    /// <summary>不處理的項目：已經匯到別的專案，或它的上層在別的專案。連留言與檔案都不補。</summary>
    public HashSet<string> Excluded { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 一個專案的檔案與留言（交接第 6.5、6.6 節）：每個項目先上傳要搬的檔案、再組出留言送出。
/// 已經匯入過的（用 lookup 問過）不再處理，所以重跑只補新的、也不重傳檔案。
/// </summary>
internal sealed class ContentImporter(
    HistoryApi api, PersonDirectory people, ImportPlan plan, RunOptions options, ImportReport report, ImportState state, Action<string> progress)
{
    private const int CommentBatchSize = 500;
    private const int MaxAttachmentsPerItem = 100;

    public async Task ImportAsync(ProjectPlan project)
    {
        var tally = report.Project(project.Info.Key);
        var items = project.Items.Where(item => !state.Excluded.Contains(item.ExternalId)).ToArray();
        var known = items.Where(item => state.PreExisting.Contains(item.ExternalId)).ToArray();
        var comments = await api.LookupAsync("comment", known.SelectMany(item => CommentThreads.Flatten(item.Source).Select(comment => comment.Id)));
        var attachments = await api.LookupAsync("attachment", known.SelectMany(item => AssetsOf(item).Select(asset => asset.Id)));
        var composer = new CommentComposer(people, report.Reminders);
        var pending = new List<JsonNode>();
        var done = 0;
        foreach (var item in items)
        {
            await ImportItemAsync(project, item, comments, attachments, composer, pending, tally);
            if (++done % 25 == 0)
            {
                progress($"專案 {project.Info.Key}：留言與檔案 {done}/{items.Length} 個項目");
            }
        }

        await FlushAsync(project, pending, tally);
    }

    private async Task ImportItemAsync(
        ProjectPlan project, PlannedItem item, IReadOnlyDictionary<string, ImportedRef> knownComments,
        IReadOnlyDictionary<string, ImportedRef> knownAttachments, CommentComposer composer, List<JsonNode> pending, ProjectTally tally)
    {
        var threads = CommentThreads.Flatten(item.Source);
        var fresh = threads.Where(comment => !knownComments.ContainsKey(comment.Id)).ToArray();
        tally.CommentsSkipped += threads.Count - fresh.Length;
        var where = state.ItemKeys.GetValueOrDefault(item.ExternalId) ?? $"monday {item.ExternalId}";
        var outcomes = await ResolveAssetsAsync(item, where, fresh, knownAttachments, tally);
        foreach (var comment in fresh)
        {
            var author = AuthorOf(comment);
            var body = composer.Compose(comment, author, outcomes, where);
            var json = new JsonObject
            {
                ["externalId"] = comment.Id,
                ["itemExternalId"] = item.ExternalId,
                ["authorId"] = author.Person.Id.ToString(),
                ["at"] = HistoryApi.Moment(comment.At),
                ["body"] = body,
            };
            if (comment.ReplyToId is not null)
            {
                json["replyToExternalId"] = comment.ReplyToId;
            }

            if (options.DryRun)
            {
                tally.CommentsCreated++;
                continue;
            }

            pending.Add(json);
            if (pending.Count >= CommentBatchSize)
            {
                await FlushAsync(project, pending, tally);
            }
        }
    }

    private async Task FlushAsync(ProjectPlan project, List<JsonNode> pending, ProjectTally tally)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var result = await api.ImportCommentsAsync(project.Info.Key, pending);
        pending.Clear();
        tally.CommentsCreated += result.Created;
        tally.CommentsSkipped += result.Skipped;
    }

    private static string SkipReason(string reason) => reason switch
    {
        "too_large" => "超過 25 MiB，fetch 沒有下載",
        "download_failed" => "fetch 下載失敗",
        "no_url" => "monday 沒有給下載網址",
        "io_error" => "fetch 存檔失敗（檔名太長或磁碟問題）",
        _ => $"fetch 沒有下載（{reason}）",
    };

    private static bool IsAboutTheFileItself(CliException exception) =>
        exception.HttpStatus == 413
        || exception.Code is "attachment.too_large" or "attachment.limit_reached" or "attachment.name_invalid";

    private ResolvedPerson AuthorOf(FlatComment comment) =>
        people.Author(comment.CreatorId) ?? throw new InvalidOperationException($"留言 {comment.Id} 的作者對不上（檢查階段應該已經擋下）。");

    private static IEnumerable<MondayAsset> AssetsOf(PlannedItem item) =>
        item.Source.Assets.Concat(CommentThreads.Flatten(item.Source).SelectMany(comment => comment.Assets));

    /// <summary>
    /// 這個項目要搬的檔案：項目本身的，加上新留言裡的。已經匯入過的直接用它的編號；沒辦法搬的（沒下載、太大、數量到上限、伺服器拒絕）
    /// 記成提醒，留言裡照實寫。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, AssetOutcome>> ResolveAssetsAsync(
        PlannedItem item, string where, IReadOnlyList<FlatComment> fresh, IReadOnlyDictionary<string, ImportedRef> known, ProjectTally tally)
    {
        // 項目的檔案若也出現在留言裡，以留言那邊為準（署名與時間是留言的）；項目層級只匯留言裡沒有的。
        var inComments = CommentThreads.Flatten(item.Source).SelectMany(comment => comment.Assets).Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        var wanted = item.Source.Assets.Where(asset => !inComments.Contains(asset.Id))
            .Select(asset => (Asset: asset, Uploader: (people.Match(asset.UploadedById) ?? item.Creator.Person).Id, At: asset.CreatedAt ?? item.Source.CreatedAt))
            .Concat(fresh.SelectMany(comment => comment.Assets.Select(asset => (Asset: asset, Uploader: AuthorOf(comment).Person.Id, At: comment.At))));
        var outcomes = new Dictionary<string, AssetOutcome>(StringComparer.Ordinal);
        var attached = 0;
        foreach (var (asset, uploader, at) in wanted)
        {
            if (outcomes.ContainsKey(asset.Id))
            {
                continue;
            }

            if (known.TryGetValue(asset.Id, out var existing))
            {
                attached++;
                tally.AttachmentsSkipped++;
                outcomes[asset.Id] = new AssetOutcome(asset.Name, existing.TargetId);
                continue;
            }

            var itemKey = state.ItemKeys.GetValueOrDefault(item.ExternalId);
            var (id, reason) = await UploadAsync(asset, itemKey, uploader, at, attached);
            if (id is null)
            {
                tally.AttachmentsNotMoved++;
                report.Reminders.Add(NoteKinds.NotMoved, $"檔案「{asset.Name}」（{where}）沒有搬過來：{reason}");
            }
            else
            {
                attached++;
                tally.AttachmentsCreated++;
            }

            outcomes[asset.Id] = new AssetOutcome(asset.Name, id);
        }

        return outcomes;
    }

    /// <summary>上傳一個檔案。回傳附件的編號；沒搬時是 null 加原因。乾跑不上傳，用假的編號代表「會搬」。</summary>
    private async Task<(Guid? Id, string? Reason)> UploadAsync(MondayAsset asset, string? itemKey, Guid uploader, DateTimeOffset at, int attached)
    {
        if (asset.LocalPath is null)
        {
            return (null, plan.SkippedFiles.TryGetValue(asset.Id, out var reason) ? SkipReason(reason) : "dump 裡沒有這個檔案（fetch 沒加 --files？），或它是符號連結、不在 dump 資料夾裡");
        }

        if (AttachmentFiles.Problem(asset.LocalPath) is { } problem)
        {
            return (null, problem);
        }

        if (attached >= MaxAttachmentsPerItem)
        {
            return (null, $"項目的附件已經到 {MaxAttachmentsPerItem} 個的上限");
        }

        if (options.DryRun)
        {
            return (Guid.Empty, null);
        }

        var fileName = AttachmentFiles.SafeFileName(asset.Name, asset.Id);
        try
        {
            var stream = File.OpenRead(asset.LocalPath);
            return (await api.UploadAttachmentAsync(itemKey ?? throw new InvalidOperationException($"項目 {asset.Id} 還沒有項目編號。"), asset.Id, uploader, at, stream, fileName, AttachmentFiles.ContentTypeOf(fileName)), null);
        }
        catch (CliException exception) when (IsAboutTheFileItself(exception))
        {
            // 太大、數量到上限、檔名不合：這個檔案不搬，其他照樣匯；留言裡寫明。
            // 其他的錯誤（例如 history.invalid：請求本身有問題）不能當成「這個檔案不搬」，讓整個 run 失敗。
            return (null, $"伺服器拒絕：{exception.Message}");
        }
    }
}
