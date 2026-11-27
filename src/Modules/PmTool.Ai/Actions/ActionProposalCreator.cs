using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.Actions;

/// <summary>
/// 建立動作建議（<see cref="IActionProposalCreator"/>）：依種類交給處理器檢查、產生步驟，通用的部分（專案看得到、理由、步數）在這裡。只存、不寫入任何東西。
/// </summary>
internal sealed class ActionProposalCreator(
    IDbContextFactory<AiDbContext> contexts,
    IProjectService projects,
    IEnumerable<IActionProposalHandler> handlers,
    TimeProvider time) : IActionProposalCreator
{
    private const int MaxReasonLength = 200;

    public async Task<Result<ActionProposal>> CreateAsync(Actor actor, ActionProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var handler = handlers.LastOrDefault(h => h.Kind == draft.Kind);
        if (handler is null)
        {
            return Invalid("kind", "不認得這種建議。");
        }

        if (draft.Payload.ValueKind != JsonValueKind.Object)
        {
            return Invalid("payload", "參數要是一個物件。");
        }

        var reason = CheckReason(draft.Reason);
        if (reason.Failure is { } badReason)
        {
            return badReason;
        }

        var projectKey = draft.ProjectKey;
        if (projectKey is not null)
        {
            var project = await projects.GetAsync(actor, projectKey, cancellationToken);
            if (project.Failure is { } notFound)
            {
                return notFound;
            }

            projectKey = project.Value.Project.Key;
        }

        var prepared = await handler.PrepareAsync(new ActionContext(actor, projectKey, draft.ConversationId), draft.Payload, cancellationToken);
        if (prepared.Failure is { } rejected)
        {
            return rejected;
        }

        if (prepared.Value.Steps.Count is < 1 or > AiRules.MaxActionSteps)
        {
            return Invalid("changes", $"一份建議要有 1 到 {AiRules.MaxActionSteps} 步。");
        }

        var row = new ActionProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            Kind = handler.Kind,
            ProjectKey = projectKey,
            Title = AiText.Cut(prepared.Value.Title, AiRules.MaxErrorLength),
            Steps = ActionProposalMapper.WriteSteps(prepared.Value.Steps),
            State = prepared.Value.State.GetRawText(),
            Inputs = ActionProposalMapper.WriteInputs(prepared.Value.Inputs),
            Reason = reason.Value,
            Status = ActionProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.ActionProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ActionProposalMapper.ToProposal(row, time.GetUtcNow());
    }

    private static Result<string?> CheckReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (string?)null;
        }

        if (text.Length > MaxReasonLength || text.Any(char.IsControl))
        {
            return Invalid("reason", $"理由要寫成一行、最多 {MaxReasonLength} 字。");
        }

        return text;
    }

    private static Failure Invalid(string path, string message) =>
        new(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "動作建議不合規定，沒有建立。")
        {
            Details = [new FailureDetail(AiErrorCodes.FieldInvalid, path, message)],
        };
}
