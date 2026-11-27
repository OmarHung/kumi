using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Actions.People;
using PmTool.Ai.Attachments;
using PmTool.Work;

namespace PmTool.Ai.Actions.Attachments;

/// <summary>
/// 把模型給的附件變更整理成步驟與卡片文字。先擋項目或頁面看不看得到、能不能寫（項目要 item.update、專案沒封存；頁面要能編輯），
/// 套用時各模組再照使用者的權限檢查一次。attach 的檔案只認這則對話裡使用者自己上傳的（<see cref="ConversationAttachmentReader"/>）。
/// </summary>
internal sealed class AttachmentPlanner(
    ActionContext context,
    ConversationAttachmentReader conversation,
    IWorkItemService items,
    IProjectService projects,
    IAttachmentService itemAttachments,
    IServiceProvider services)
{
    private static readonly string[] Properties = ["action", "attachmentId", "itemKey", "spaceKey", "pageNumber"];

    private readonly List<FailureDetail> _issues = [];
    private readonly List<string> _forbidden = [];
    private readonly List<AttachmentStep> _steps = [];
    private readonly List<ActionStep> _cards = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectDetail?> _projectCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<Attachment>?> _itemFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _adding = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AssistantAttachment>? _uploaded;

    public async Task<Result<(IReadOnlyList<AttachmentStep> Steps, IReadOnlyList<ActionStep> Cards)>> PlanAsync(
        IReadOnlyList<JsonElement> changes, CancellationToken cancellationToken)
    {
        for (var i = 0; i < changes.Count; i++)
        {
            await ReadChangeAsync(changes[i], i, cancellationToken);
        }

        if (_forbidden.Count > 0)
        {
            return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, _forbidden[0]) { Details = _issues };
        }

        return _issues.Count > 0 ? PayloadJson.Invalid("附件建議不合規定，沒有建立。", _issues) : (_steps, _cards);
    }

    private async Task ReadChangeAsync(JsonElement change, int index, CancellationToken cancellationToken)
    {
        var path = $"changes[{index}]";
        if (change.ValueKind != JsonValueKind.Object)
        {
            _issues.Add(PayloadJson.Issue(path, "每一筆要是物件。"));
            return;
        }

        PayloadJson.Unknown(change, Properties, path, _issues);
        var action = PayloadJson.Text(change, "action", path, _issues, required: true);
        var idOrName = PayloadJson.Text(change, "attachmentId", path, _issues, required: true);
        var itemKey = PayloadJson.Text(change, "itemKey", path, _issues);
        var spaceKey = PayloadJson.Text(change, "spaceKey", path, _issues);
        var page = ReadPageNumber(change, path);
        if (action is null || idOrName is null)
        {
            return;
        }

        switch (action)
        {
            case AttachmentStep.Attach:
                await AttachAsync(path, idOrName, itemKey, spaceKey, page, cancellationToken);
                break;
            case AttachmentStep.Delete:
                await DeleteAsync(path, idOrName, itemKey, spaceKey, page, cancellationToken);
                break;
            default:
                _issues.Add(PayloadJson.Issue($"{path}.action", "action 要是 attach 或 delete。"));
                break;
        }
    }

    private async Task AttachAsync(
        string path, string idOrName, string? itemKey, string? spaceKey, int? page, CancellationToken cancellationToken)
    {
        if ((itemKey is null) == (spaceKey is null))
        {
            _issues.Add(PayloadJson.Issue(path, "attach 要給 itemKey（附到項目），或 spaceKey 加 pageNumber（附到知識庫頁面），兩種擇一。"));
            return;
        }

        if (itemKey is not null && page is not null)
        {
            _issues.Add(PayloadJson.Issue($"{path}.pageNumber", "附到項目時不要給 pageNumber。"));
            return;
        }

        if (itemKey is null && page is null)
        {
            _issues.Add(PayloadJson.Issue($"{path}.pageNumber", "附到頁面要給 pageNumber。"));
            return;
        }

        _uploaded ??= await conversation.ListAsync(context.Actor, context.ConversationId, cancellationToken);
        var file = ConversationAttachmentReader.Resolve(_uploaded, idOrName);
        if (file.Failure is { } missing)
        {
            _issues.Add(PayloadJson.Issue($"{path}.attachmentId", missing.Message));
            return;
        }

        var target = itemKey is not null
            ? await ItemTargetAsync(path, itemKey, writeCount: 1, cancellationToken)
            : await PageTargetAsync(path, spaceKey!, page!.Value, cancellationToken);
        if (target is null)
        {
            return;
        }

        if (!_seen.Add($"attach|{file.Value.Id}|{target.Key}"))
        {
            _issues.Add(PayloadJson.Issue(path, $"「{file.Value.FileName}」附到 {target.Label} 在這份建議裡出現了不只一次。"));
            return;
        }

        _steps.Add(new AttachmentStep
        {
            Action = AttachmentStep.Attach,
            AttachmentId = file.Value.Id,
            FileName = file.Value.FileName,
            ItemKey = target.ItemKey,
            ProjectKey = target.ProjectKey,
            SpaceKey = target.SpaceKey,
            PageNumber = target.PageNumber,
        });
        _cards.Add(new ActionStep("附加檔案", target.Label, null, $"{file.Value.FileName}（{Size(file.Value.SizeBytes)}）", null, ActionTextFormat.Text));
    }

    private async Task DeleteAsync(
        string path, string idOrName, string? itemKey, string? spaceKey, int? page, CancellationToken cancellationToken)
    {
        if (itemKey is null || spaceKey is not null || page is not null)
        {
            _issues.Add(PayloadJson.Issue(path, "delete 只能刪項目的附件，要給 itemKey（不給 spaceKey、pageNumber）。"));
            return;
        }

        var target = await ItemTargetAsync(path, itemKey, writeCount: 0, cancellationToken);
        if (target is null || !_itemFiles.TryGetValue(target.ItemKey!, out var files) || files is null)
        {
            return;
        }

        var matches = Guid.TryParse(idOrName, out var id)
            ? files.Where(f => f.Id == id).ToList()
            : files.Where(f => string.Equals(f.FileName, idOrName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
        {
            var list = string.Join("、", files.Select(f => $"{f.FileName}（{f.Id}）"));
            var message = matches.Count == 0
                ? $"{target.ItemKey} 沒有這個附件（attachmentId 用 get_item 回的）。現有的：{(files.Count == 0 ? "沒有附件" : list)}。"
                : $"{target.ItemKey} 有 {matches.Count} 個叫「{idOrName}」的附件，請改用編號。現有的：{list}。";
            _issues.Add(PayloadJson.Issue($"{path}.attachmentId", message));
            return;
        }

        var file = matches[0];
        if (!_seen.Add($"delete|{file.Id}"))
        {
            _issues.Add(PayloadJson.Issue(path, $"附件「{file.FileName}」在這份建議裡出現了不只一次。"));
            return;
        }

        _steps.Add(new AttachmentStep { Action = AttachmentStep.Delete, AttachmentId = file.Id, FileName = file.FileName, ItemKey = target.ItemKey, ProjectKey = target.ProjectKey });
        _cards.Add(new ActionStep(
            "刪除附件", target.Label, file.FileName, null, "刪除後不能復原：檔案會從儲存空間移除，只有備份裡還留著。", ActionTextFormat.Text));
    }

    private async Task<Target?> ItemTargetAsync(string path, string itemKey, int writeCount, CancellationToken cancellationToken)
    {
        var found = await items.GetAsync(context.Actor, itemKey, cancellationToken);
        if (found.Failure is not null
            || (context.ProjectKey is { } scope && !string.Equals(found.Value.ProjectKey, scope, StringComparison.OrdinalIgnoreCase)))
        {
            _issues.Add(PayloadJson.Issue($"{path}.itemKey", PmTool.Ai.Items.ItemLookup.NotFound(itemKey).Message));
            return null;
        }

        var item = found.Value;
        var project = await ProjectAsync(item.ProjectKey, cancellationToken);
        if (project is null)
        {
            _issues.Add(PayloadJson.Issue($"{path}.itemKey", PmTool.Ai.Items.ItemLookup.NotFound(itemKey).Message));
            return null;
        }

        if (project.Project.ArchivedAt is not null)
        {
            _issues.Add(PayloadJson.Issue($"{path}.itemKey", $"專案 {item.ProjectKey} 已經封存，不能改附件。"));
            return null;
        }

        if (!project.MyPermissions.Contains(WorkPermissions.ItemUpdate))
        {
            _forbidden.Add($"你在專案 {item.ProjectKey} 沒有改項目的權限（item.update），不能附加或刪除附件。");
            _issues.Add(new FailureDetail(AiErrorCodes.Forbidden, $"{path}.itemKey", _forbidden[^1]));
            return null;
        }

        if (!_itemFiles.TryGetValue(item.Key, out var files))
        {
            var listed = await itemAttachments.ListAsync(context.Actor, item.Key, cancellationToken);
            files = listed.Failure is null ? listed.Value : null;
            _itemFiles[item.Key] = files;
        }

        if (files is null)
        {
            _issues.Add(PayloadJson.Issue($"{path}.itemKey", PmTool.Ai.Items.ItemLookup.NotFound(itemKey).Message));
            return null;
        }

        var adding = _adding.GetValueOrDefault(item.Key) + writeCount;
        if (writeCount > 0 && files.Count + adding > AttachmentRules.MaxPerItem)
        {
            _issues.Add(PayloadJson.Issue($"{path}.itemKey", $"{item.Key} 的附件已經太多了（最多 {AttachmentRules.MaxPerItem} 個）。"));
            return null;
        }

        _adding[item.Key] = adding;
        return new Target($"item|{item.Key}", $"{item.Key} {AiText.Cut(item.Title, 80)}")
        {
            ItemKey = item.Key,
            ProjectKey = item.ProjectKey,
        };
    }

    private async Task<Target?> PageTargetAsync(string path, string spaceKey, int number, CancellationToken cancellationToken)
    {
        var pages = services.GetService<PmTool.Knowledge.IKnowledgePageService>();
        if (pages is null)
        {
            _issues.Add(PayloadJson.Issue($"{path}.spaceKey", "知識庫沒有啟用，不能附到頁面。"));
            return null;
        }

        var found = await pages.GetAsync(context.Actor, spaceKey, number, cancellationToken);
        if (found.Failure is not null)
        {
            _issues.Add(PayloadJson.Issue($"{path}.spaceKey", $"找不到知識庫頁面「{spaceKey} 的第 {number} 頁」。"));
            return null;
        }

        var page = found.Value;
        if (!page.MyPermissions.CanEdit)
        {
            _forbidden.Add($"你沒有編輯「{page.Space.Key}／{page.Title}」的權限，不能附加檔案。");
            _issues.Add(new FailureDetail(AiErrorCodes.Forbidden, $"{path}.spaceKey", _forbidden[^1]));
            return null;
        }

        return new Target($"page|{page.Space.Key}|{page.Number}", $"{page.Space.Key}／{AiText.Cut(page.Title, 80)}")
        {
            SpaceKey = page.Space.Key,
            PageNumber = page.Number,
        };
    }

    private async Task<ProjectDetail?> ProjectAsync(string projectKey, CancellationToken cancellationToken)
    {
        if (!_projectCache.TryGetValue(projectKey, out var detail))
        {
            var found = await projects.GetAsync(context.Actor, projectKey, cancellationToken);
            detail = found.Failure is null ? found.Value : null;
            _projectCache[projectKey] = detail;
        }

        return detail;
    }

    private int? ReadPageNumber(JsonElement change, string path)
    {
        if (!change.TryGetProperty("pageNumber", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0)
        {
            return number;
        }

        _issues.Add(PayloadJson.Issue($"{path}.pageNumber", "pageNumber 要是正整數（頁面的編號，不是標題）。"));
        return null;
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };

    private sealed record Target(string Key, string Label)
    {
        public string? ItemKey { get; init; }

        public string? ProjectKey { get; init; }

        public string? SpaceKey { get; init; }

        public int? PageNumber { get; init; }
    }
}
