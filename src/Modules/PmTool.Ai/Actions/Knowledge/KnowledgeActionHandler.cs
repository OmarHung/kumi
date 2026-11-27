using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Knowledge;

namespace PmTool.Ai.Actions.Knowledge;

/// <summary>
/// 知識庫的動作建議（internal/HANDOFF-ai-coverage-2.md 4.2）：頁面新增、修改、搬移、刪除、復原、改限制，空間新增、改設定、封存與取消封存。
/// 建立時照使用者的權限與現況檢查（<see cref="KnowledgePlanner"/>）；套用時依序呼叫知識庫的服務，一步一步做、每步各自的結果。
/// 服務向容器現要：沒有知識庫模組的測試主機也能建好處理器。
/// </summary>
internal sealed class KnowledgeActionHandler(IServiceProvider services) : IActionProposalHandler
{
    // 處理器是 Scoped：一次套用的所有步驟在同一個範圍裡，前面 create_page 真的建出來的頁碼留在這裡，後面用 parentRef 的步驟讀。
    // 鍵是（整份狀態的雜湊、ref），不同建議不會互相蓋到。
    private readonly ConcurrentDictionary<string, int> _createdNumbers = new(StringComparer.Ordinal);

    public string Kind => ActionKinds.Knowledge;

    public async Task<Result<PreparedAction>> PrepareAsync(ActionContext context, JsonElement payload, CancellationToken cancellationToken)
    {
        var issues = new List<FailureDetail>();
        var read = KnowledgeChangeReader.Read(payload, issues);
        if (issues.Count > 0)
        {
            return Invalid(issues, forbidden: false);
        }

        var planner = new KnowledgePlanner(
            new KnowledgeLookup(
                services.GetRequiredService<IKnowledgeSpaceService>(),
                services.GetRequiredService<IKnowledgePageService>(),
                services.GetRequiredService<IPeopleDirectory>(),
                context.Actor),
            context.Actor);
        var planned = new List<PlannedChange>(read.Count);
        for (var i = 0; i < read.Count; i++)
        {
            if (await planner.PlanAsync(read[i]!, i, cancellationToken) is { } change)
            {
                planned.Add(change);
            }
        }

        if (planner.Issues.Count > 0)
        {
            return Invalid(planner.Issues, planner.HasForbidden);
        }

        var steps = planned.Select(p => p.Step).ToList();
        var state = JsonSerializer.SerializeToElement(planned.Select(p => p.Change).ToList(), KnowledgeChange.Json);
        return new PreparedAction(TitleOf(planned), steps, state, []);
    }

    public async Task<ActionStepResult> ApplyStepAsync(
        ActionContext context, JsonElement state, int index, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken)
    {
        var changes = state.Deserialize<List<KnowledgeChange>>(KnowledgeChange.Json) ?? [];
        var change = changes[index];
        var spaces = services.GetRequiredService<IKnowledgeSpaceService>();
        var pages = services.GetRequiredService<IKnowledgePageService>();
        var stateKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state.GetRawText())));
        var actor = context.Actor;
        return change.Action switch
        {
            KnowledgeActions.CreatePage => await CreatePageAsync(pages, actor, change, stateKey, cancellationToken),
            KnowledgeActions.UpdatePage => PageResult(
                await pages.UpdateAsync(actor, change.SpaceKey!, change.Number!.Value, new PageChanges(change.Title, change.Body, change.ExpectedRevision!.Value), cancellationToken),
                change.SpaceKey!),
            KnowledgeActions.MovePage => await MovePageAsync(pages, actor, change, stateKey, cancellationToken),
            KnowledgeActions.DeletePage => Plain(await pages.DeleteAsync(actor, change.SpaceKey!, change.Number!.Value, cancellationToken), null),
            KnowledgeActions.RestorePage => PageResult(await pages.RestoreAsync(actor, change.SpaceKey!, change.Number!.Value, cancellationToken), change.SpaceKey!),
            KnowledgeActions.SetRestriction => Plain(
                await pages.SetRestrictionAsync(actor, change.SpaceKey!, change.Number!.Value, change.People is { Count: > 0 } ? change.People : null, cancellationToken),
                Link(change.SpaceKey!, change.Number)),
            KnowledgeActions.CreateSpace => SpaceResult(
                await spaces.CreateAsync(actor, new NewSpace(change.Key!, change.Name!, change.Description, change.ProjectKey), cancellationToken)),
            KnowledgeActions.UpdateSpace => SpaceResult(
                await spaces.UpdateAsync(actor, change.SpaceKey!, new SpaceSettings(change.Name!, change.Description), cancellationToken)),
            KnowledgeActions.ArchiveSpace => SpaceResult(await spaces.SetArchivedAsync(actor, change.SpaceKey!, true, cancellationToken)),
            _ => SpaceResult(await spaces.SetArchivedAsync(actor, change.SpaceKey!, false, cancellationToken)),
        };
    }

    private async Task<ActionStepResult> CreatePageAsync(
        IKnowledgePageService pages, Actor actor, KnowledgeChange change, string stateKey, CancellationToken cancellationToken)
    {
        if (!TryResolveParent(change, stateKey, out var parentNumber, out var skipped))
        {
            return skipped!;
        }

        var page = new NewPage(parentNumber, change.Title!, change.Body) { BeforeNumber = change.BeforeNumber };
        var created = await pages.CreateAsync(actor, change.SpaceKey!, page, cancellationToken);
        if (created.Failure is { } failure)
        {
            return ActionStepResult.Failed(failure);
        }

        if (change.Ref is not null)
        {
            _createdNumbers[$"{stateKey}|{change.Ref}"] = created.Value.Number;
        }

        return ActionStepResult.Done(Link(change.SpaceKey!, created.Value.Number));
    }

    private async Task<ActionStepResult> MovePageAsync(
        IKnowledgePageService pages, Actor actor, KnowledgeChange change, string stateKey, CancellationToken cancellationToken)
    {
        if (!TryResolveParent(change, stateKey, out var parentNumber, out var skipped))
        {
            return skipped!;
        }

        var moved = await pages.MoveAsync(actor, change.SpaceKey!, change.Number!.Value, new PageMove(parentNumber, change.BeforeNumber), cancellationToken);
        return moved.Failure is { } failure ? ActionStepResult.Failed(failure) : ActionStepResult.Done(Link(change.SpaceKey!, change.Number));
    }

    /// <summary>上層用 parentRef 時換成前面真的建出來的頁碼；那一步沒成功就跳過這一步（掛在失敗頁面底下的不做）。</summary>
    private bool TryResolveParent(KnowledgeChange change, string stateKey, out int? parentNumber, out ActionStepResult? skipped)
    {
        parentNumber = change.ParentNumber;
        skipped = null;
        if (change.ParentRef is null)
        {
            return true;
        }

        if (_createdNumbers.TryGetValue($"{stateKey}|{change.ParentRef}", out var number))
        {
            parentNumber = number;
            return true;
        }

        skipped = ActionStepResult.Failed(new Failure(
            FailureKind.Conflict, AiErrorCodes.FieldInvalid, $"前面新增的頁面（ref「{change.ParentRef}」）沒有建立成功，這一步跳過。"));
        return false;
    }

    private static ActionStepResult PageResult(Result<PageDetail> result, string spaceKey) =>
        result.Failure is { } failure ? ActionStepResult.Failed(ForCard(failure)) : ActionStepResult.Done(Link(spaceKey, result.Value.Number));

    // 知識庫的衝突訊息是寫給網頁編輯器的（「再決定要不要覆蓋」），卡片上再按一次只會再被擋：改成告訴使用者下一步。
    private static Failure ForCard(Failure failure) => failure.Code == KnowledgeErrorCodes.PageConflict
        ? failure with { Message = "這一頁在建議提出之後被別人改過，這次沒有套用（不會蓋掉別人改的）。請助理重新讀這一頁再提一次。" }
        : failure;

    private static ActionStepResult SpaceResult(Result<SpaceSummary> result) =>
        result.Failure is { } failure ? ActionStepResult.Failed(failure) : ActionStepResult.Done(Link(result.Value.Key, null));

    private static ActionStepResult Plain<T>(Result<T> result, string? link) =>
        result.Failure is { } failure ? ActionStepResult.Failed(failure) : ActionStepResult.Done(link);

    private static string Link(string spaceKey, int? number) => number is { } n ? $"/knowledge/{spaceKey}/{n}" : $"/knowledge/{spaceKey}";

    private static string TitleOf(IReadOnlyList<PlannedChange> planned)
    {
        var keys = planned
            .Select(p => p.Change.SpaceKey ?? p.Change.Key)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return keys.Count == 0 ? "建議調整知識庫" : $"建議調整知識庫（{string.Join("、", keys)}）";
    }

    private static Failure Invalid(IReadOnlyList<FailureDetail> issues, bool forbidden) =>
        forbidden
            ? new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "知識庫建議裡有你沒有權限做的事，沒有建立。") { Details = issues }
            : new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "知識庫建議不合規定，沒有建立。") { Details = issues };
}
