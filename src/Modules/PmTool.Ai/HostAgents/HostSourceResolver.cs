using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Ai.Providers;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// 選的連線編號是這個組織的主機來源時的檢查（<see cref="AiSelectionResolver"/> 委派過來）：發問的人現在是有效成員、
/// 來源開著而且能用、模型在開放而且引擎回報的清單裡、思考深度在那個模型的清單裡。額度與排隊另外在送出時預留（<see cref="HostReservations"/>）。
/// 不是來源的編號回 null，讓呼叫端照原本的流程（組織連線、自己的連線）處理。
/// </summary>
internal sealed class HostSourceResolver(IDbContextFactory<AiDbContext> contexts, HostPeople people, TimeProvider time)
{
    public async Task<Result<ResolvedSelection>?> TryResolveAsync(Actor actor, AiSelection selection, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var view = await HostReadiness.LoadViewAsync(db, actor.OrganizationId, selection.ConnectionId, cancellationToken);
        if (view is null)
        {
            return null;
        }

        // 訪客與停用的人：當作沒有這個連線，不透露它存在。
        if (!await people.IsMemberAsync(actor.OrganizationId, actor.PersonId, cancellationToken))
        {
            return Invalid("選的連線已經不存在，請換一個。");
        }

        var (status, _) = HostReadiness.SourceStatus(view, quotaReached: false, time.GetUtcNow());
        if (status != HostSourceStatus.Ready)
        {
            return HostFailures.SourceUnavailable(HostReadiness.MemberNotice(status, view.Engine.Readiness) ?? "這個來源現在不能用。");
        }

        var model = HostSourceOptions.ModelsOf(view).FirstOrDefault(m => m.Id == selection.Model);
        if (model is null)
        {
            return Invalid($"模型「{selection.Model}」現在沒有開放，請換一個。");
        }

        if (selection.Effort is { } effort && !model.Efforts.Contains(effort))
        {
            return Invalid($"模型「{model.DisplayName}」沒有「{AiEnumNames.NameOf(effort)}」這個思考深度，請換一個。");
        }

        var kind = AiEnumNames.ParseKind(view.Source.Engine);
        return new ResolvedSelection(
            selection,
            new AiEndpoint(kind, null, null) { HostSourceId = view.Source.Id },
            view.Source.Name,
            new AiModel(model.Id, model.DisplayName, model.Efforts, Enabled: true, Custom: false));
    }

    private static Failure Invalid(string message) => new(FailureKind.Invalid, AiErrorCodes.SelectionInvalid, message);
}
