using PmTool.Abstractions;
using PmTool.Knowledge;

namespace PmTool.Ai.Actions.Knowledge;

/// <summary>檢查過、整理好的一個動作，和它在卡片上的一步。</summary>
internal sealed record PlannedChange(KnowledgeChange Change, ActionStep Step);

/// <summary>前面的 create_page 用 ref 留下的資訊，後面的動作用 parentRef 掛在它底下時查。</summary>
internal sealed record CreatedPageRef(string SpaceKey, string Title, int Depth);

/// <summary>一個動作要用的空間：已經存在的（<see cref="Existing"/>），或這份建議前面剛新增的（<see cref="Existing"/> 是 null，沒辦法查現況）。</summary>
internal sealed record SpaceTarget(string Key, string Name, SpaceSummary? Existing);

/// <summary>
/// 照使用者的權限與現況檢查知識庫建議的每一個動作（internal/HANDOFF-ai-coverage-2.md 4.2），產生卡片上的步驟。
/// 只擋「現在就知道會失敗」的事，讓模型一次修好；套用時知識庫模組照使用者的權限再檢查一次。
/// 每個問題一個細項，路徑 <c>changes[2].title</c>；權限問題的細項代碼是 <see cref="AiErrorCodes.Forbidden"/>。
/// </summary>
internal sealed partial class KnowledgePlanner(KnowledgeLookup lookup, Actor actor)
{
    private readonly Dictionary<string, SpaceTarget> _newSpaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CreatedPageRef> _refs = new(StringComparer.Ordinal);

    public List<FailureDetail> Issues { get; } = [];

    public bool HasForbidden => Issues.Any(issue => issue.Code == AiErrorCodes.Forbidden);

    public Task<PlannedChange?> PlanAsync(KnowledgeChange change, int index, CancellationToken cancellationToken) => change.Action switch
    {
        KnowledgeActions.CreatePage => CreatePageAsync(change, index, cancellationToken),
        KnowledgeActions.UpdatePage => UpdatePageAsync(change, index, cancellationToken),
        KnowledgeActions.MovePage => MovePageAsync(change, index, cancellationToken),
        KnowledgeActions.DeletePage => DeletePageAsync(change, index, cancellationToken),
        KnowledgeActions.RestorePage => RestorePageAsync(change, index, cancellationToken),
        KnowledgeActions.SetRestriction => SetRestrictionAsync(change, index, cancellationToken),
        KnowledgeActions.CreateSpace => CreateSpaceAsync(change, index, cancellationToken),
        KnowledgeActions.UpdateSpace => UpdateSpaceAsync(change, index, cancellationToken),
        _ => ArchiveSpaceAsync(change, index, cancellationToken),
    };

    private void Issue(int index, string field, string message) =>
        Issues.Add(new FailureDetail(AiErrorCodes.FieldInvalid, Path(index, field), message));

    private void Forbid(int index, string field, string message) =>
        Issues.Add(new FailureDetail(AiErrorCodes.Forbidden, Path(index, field), message));

    private static string Path(int index, string field) => field.Length == 0 ? $"changes[{index}]" : $"changes[{index}].{field}";

    private static string TargetOf(string spaceKey, string title) => $"{spaceKey}／{title}";

    private static string Link(string spaceKey, int? number) => number is { } n ? $"/knowledge/{spaceKey}/{n}" : $"/knowledge/{spaceKey}";

    /// <summary>找到這個動作的空間；找不到（或這份建議前面剛新增的）都處理好了，呼叫的人只要看是不是 null。</summary>
    private async Task<SpaceTarget?> SpaceOfAsync(KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        var key = change.SpaceKey!;
        if (_newSpaces.TryGetValue(key, out var made))
        {
            return made;
        }

        var space = await lookup.SpaceAsync(key, cancellationToken);
        if (space is null)
        {
            Issue(index, "spaceKey", $"找不到空間「{key}」：它不存在，或你看不到。用 list_spaces 確認代號。");
            return null;
        }

        return new SpaceTarget(space.Key, space.Name, space);
    }

    /// <summary>寫頁面要看得到、空間沒封存、而且有編輯權限；做不到就記下原因。</summary>
    private bool CanWritePages(SpaceTarget target, int index)
    {
        if (target.Existing is not { } space)
        {
            return true;
        }

        if (space.ArchivedAt is not null || space.Project?.IsArchived == true)
        {
            Issue(index, "spaceKey", $"空間「{space.Key}」已經封存（唯讀），不能改頁面；要先取消封存。");
            return false;
        }

        if (!space.MyPermissions.CanEdit)
        {
            Forbid(index, "spaceKey", $"你在空間「{space.Key}」沒有編輯權限。組織空間要是擁有者、管理員或一般成員；專案空間要有新增項目的權限。請找有權限的人，不要叫使用者自己手動做。");
            return false;
        }

        return true;
    }

    private bool CanManageSpace(SpaceTarget target, int index, string what)
    {
        if (target.Existing is not { } space || space.MyPermissions.CanManage)
        {
            return true;
        }

        Forbid(index, "spaceKey", $"你沒有權限{what}空間「{space.Key}」：組織空間只有組織管理員，專案空間要是組織管理員或有管理專案權限的人。");
        return false;
    }

    private void CheckBody(string? body, int index)
    {
        if (body is not null && SecretText.ContainsSecret(body))
        {
            Issue(index, "body", "內文不能放私鑰或看得出格式的金鑰、權杖。");
        }
    }

    private async Task<PageDetail?> PageOfAsync(SpaceTarget target, KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        if (target.Existing is null)
        {
            Issue(index, "number", $"空間「{target.Key}」是這份建議才新增的，裡面還沒有頁面。");
            return null;
        }

        var page = await lookup.PageAsync(target.Key, change.Number!.Value, cancellationToken);
        if (page is null)
        {
            Issue(index, "number", $"找不到頁面 {target.Key}／{change.Number}：它不存在、在垃圾桶裡，或你看不到。用 get_page_tree 確認編號。");
        }

        return page;
    }
}
