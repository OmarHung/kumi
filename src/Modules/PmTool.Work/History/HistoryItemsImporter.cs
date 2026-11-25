using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.History;

/// <summary>
/// 一批歷史項目的處理。一列一列檢查、建立、存進交易（後面的列要能用前面剛建立的項目當上層，檢查上層時資料庫裡要看得到它），
/// 有任何問題呼叫端就不提交，交易退回、編號也跟著退回。乾跑也是這樣做完再退回：檢查的就是真正會發生的事。
/// 每一列先自己的檢查（外部編號、時間、成員），再交給試算表匯入的檢查（<see cref="ItemImportRules.CheckRowAsync"/>：和新增一樣的規則）。
/// </summary>
internal sealed class HistoryItemsImporter(
    WorkDbContext db, ProjectAccess access, Actor actor, HistoryItems import, IReadOnlySet<Guid> readable, TimeProvider time)
{
    private const string Rows = ItemImportRules.RowsPath;

    private readonly List<FailureDetail> _problems = [];
    private readonly List<HistoryItemRef> _created = [];
    private readonly List<HistoryItemRef> _skipped = [];
    private readonly Dictionary<string, HistoryRefRow> _existing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Guid ProjectId, string Key)> _known = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private readonly HistoryActivityIds _ids = new();
    private HashSet<string> _inBatch = new(StringComparer.Ordinal);

    public IReadOnlyList<FailureDetail> Problems => _problems;

    public IReadOnlyList<HistoryItemRef> Created => _created;

    public IReadOnlyList<HistoryItemRef> Skipped => _skipped;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _inBatch = [.. import.Rows.Select(r => r?.ExternalId).OfType<string>()];
        var parentIds = import.Rows.Select(r => r?.ParentExternalId);
        var found = await HistoryRefs.FindAsync(
            db, actor.OrganizationId, import.Source, HistoryRefKind.Item, [.. _inBatch, .. parentIds], cancellationToken);
        foreach (var (id, row) in found)
        {
            _existing[id] = row;
            _known[id] = (row.ProjectId, row.ItemKey);
        }

        for (var index = 0; index < import.Rows.Count; index++)
        {
            await ImportRowAsync(index, import.Rows[index], cancellationToken);
        }
    }

    private async Task ImportRowAsync(int index, HistoryItem? row, CancellationToken cancellationToken)
    {
        if (row?.Item is null)
        {
            Add(index, null, "這一列是空的。");
            return;
        }

        if (!IsNewExternalId(index, row))
        {
            return;
        }

        var at = HistoryRules.Normalize(row.CreatedAt);
        if (await CheckRowAsync(index, row, at, cancellationToken) is { } checkedRow)
        {
            await InsertAsync(row, at, checkedRow, cancellationToken);
        }
    }

    /// <summary>
    /// 外部編號合格、這一批裡只出現一次、之前沒匯入過，才要繼續處理這一列。已經匯入過的：在呼叫者有 project.manage 的專案就略過並帶它的編號；
    /// 其他的專案不能透露任何編號，當成這一列的問題。
    /// </summary>
    private bool IsNewExternalId(int index, HistoryItem row)
    {
        if (HistoryRules.ExternalIdProblem(row.ExternalId) is { } idProblem)
        {
            Add(index, "external_id", idProblem);
            return false;
        }

        if (!_seen.Add(row.ExternalId))
        {
            Add(index, "external_id", $"外部編號{TextRules.Quote(row.ExternalId)}在這一批裡出現不只一次。");
            return false;
        }

        if (!_existing.TryGetValue(row.ExternalId, out var already))
        {
            return true;
        }

        if (readable.Contains(already.ProjectId))
        {
            _skipped.Add(new HistoryItemRef(row.ExternalId, already.ItemKey));
        }
        else
        {
            Add(index, "external_id", HistoryRules.ImportedElsewhere(row.ExternalId));
        }

        return false;
    }

    /// <summary>這一列自己的檢查加上和新增一樣的檢查；有任何問題回 null（問題已經記下，後面的列不能拿它當上層）。</summary>
    private async Task<CheckedImportRow?> CheckRowAsync(int index, HistoryItem row, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var problemsBefore = _problems.Count;
        var parentKey = ResolveParent(index, row);
        await CheckPeopleAsync(index, row, cancellationToken);
        if (HistoryRules.TimeProblem(at, time) is { } timeProblem)
        {
            Add(index, "created_at", timeProblem);
        }

        // 上層一律由 parentExternalId 決定：給了 parentKey 是用錯了欄位，不能默默用其中一個。
        if (row.Item.ParentKey is not null)
        {
            Add(index, "parent", "歷史匯入的上層要用 parentExternalId（外部編號），不是 parentKey。");
        }

        var imported = new ImportedItem { Item = row.Item with { ParentKey = parentKey }, State = row.State };
        var (checkedRow, rowProblems) = await ItemImportRules.CheckRowAsync(db, access, imported, index, cancellationToken);
        _problems.AddRange(rowProblems);
        if (checkedRow is not null && _problems.Count == problemsBefore)
        {
            return checkedRow;
        }

        _failed.Add(row.ExternalId);
        return null;
    }

    private async Task InsertAsync(HistoryItem row, DateTimeOffset at, CheckedImportRow checkedRow, CancellationToken cancellationToken)
    {
        var author = actor with { PersonId = row.CreatedBy };
        var assignee = checkedRow.Changes.AssigneeId.Value;
        var followers = row.Followers.Where(f => f != assignee).Distinct().ToArray();
        var stamp = new HistoryStamp(import.Source, at, followers, _ids);
        var item = await ItemCreation.InsertAsync(
            db, time, author, access, checkedRow.ItemType, checkedRow.Changes, checkedRow.State, imported: true, cancellationToken, stamp);
        db.HistoryRefs.Add(HistoryRefs.New(actor, import.Source, HistoryRefKind.Item, row.ExternalId, access.Project.Id, item.Id, item.Id, WorkClock.Now(time)));

        // 存進交易（還沒提交）：後面的列檢查上層時要在資料庫裡找得到它。追蹤清掉，一千列不必一直背著前面的。
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        var key = ItemKeys.Format(access.Project.Key, item.Number);
        _created.Add(new HistoryItemRef(row.ExternalId, key));
        _known[row.ExternalId] = (access.Project.Id, key);
    }

    /// <summary>上層的項目編號；沒有上層或有問題時是 null（問題已經記下）。</summary>
    private string? ResolveParent(int index, HistoryItem row)
    {
        if (row.ParentExternalId is not { } parentId)
        {
            return null;
        }

        if (_known.TryGetValue(parentId, out var parent))
        {
            if (parent.ProjectId == access.Project.Id)
            {
                return parent.Key;
            }

            // 管不到的專案不透露它的項目編號。
            var where = readable.Contains(parent.ProjectId) ? $"（{parent.Key}）" : string.Empty;
            Add(index, "parent_external_id", $"上層{TextRules.Quote(parentId)}已經匯入到別的專案{where}，上層要在同一個專案。");
            return null;
        }

        var message = _failed.Contains(parentId)
            ? $"上層{TextRules.Quote(parentId)}那一列有問題，請先改好它。"
            : _inBatch.Contains(parentId)
                ? $"上層{TextRules.Quote(parentId)}要排在這一列的前面。"
                : $"找不到上層{TextRules.Quote(parentId)}：它要在這一批的前面，或之前已經匯入過。";
        Add(index, "parent_external_id", message);
        return null;
    }

    /// <summary>建立的人與關注的人都要是專案成員（負責人與自訂欄位的人由新增的檢查負責）。</summary>
    private async Task CheckPeopleAsync(int index, HistoryItem row, CancellationToken cancellationToken)
    {
        if (row.Followers.Count > HistoryRules.MaxFollowers)
        {
            Add(index, "followers", $"最多關注 {HistoryRules.MaxFollowers} 個人，這一列有 {row.Followers.Count} 個。");
            return;
        }

        PersonReference[] people =
        [
            new("created_by", "建立的人", row.CreatedBy),
            .. row.Followers.Select((person, position) => new PersonReference($"followers/{position}", "關注的人", person)),
        ];
        var details = await MemberRules.CheckAsync(db, access, people, cancellationToken);
        _problems.AddRange(details.Select(d => d with { Path = $"{Rows}/{index}/{d.Path}" }));
    }

    private void Add(int index, string? field, string message) => _problems.Add(HistoryRules.Problem(Rows, index, field, message));
}
