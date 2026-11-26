using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.ProjectProposals;

/// <summary>建立新專案建議（<see cref="IProjectProposalCreator"/>）。只存、不建專案；規則的檢查交給工作引擎，只有一份。</summary>
internal sealed class ProjectProposalCreator(
    IDbContextFactory<AiDbContext> contexts, IProjectService projects, ITemplateLibrary templates, TimeProvider time) : IProjectProposalCreator
{
    public async Task<Result<ProjectProposal>> CreateAsync(Actor actor, ProjectProposalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        if (!actor.IsOrganizationAdmin)
        {
            return new Failure(FailureKind.Forbidden, AiErrorCodes.AssistantForbidden, "只有組織管理員可以開新專案，請把建議交給組織管理員。");
        }

        // PostgreSQL 不收 NUL；在這裡先擋，不要等到寫入時才變成 500（工作引擎也會檢查，這是第二道）。
        if (HasControlCharacters(draft.Key, allowLineBreaks: false)
            || HasControlCharacters(draft.Name, allowLineBreaks: false)
            || HasControlCharacters(draft.Template, allowLineBreaks: false)
            || HasControlCharacters(draft.Description, allowLineBreaks: true))
        {
            return new Failure(FailureKind.Invalid, AiErrorCodes.MessageInvalid, "新專案的代號、名稱、模板或說明裡有不能用的控制字元。");
        }

        var newProject = new NewProject(draft.Key.Trim(), draft.Name.Trim(), draft.Template.Trim(), draft.IncludeExamples)
        {
            Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description,
        };
        var checkedProject = await projects.CheckNewProjectAsync(actor, newProject, cancellationToken);
        if (checkedProject.Failure is { } failure)
        {
            return failure;
        }

        // 檢查過了才存：模板一定找得到，顯示名稱存下來給卡片用（之後模板改名也不影響這張卡片）。
        var template = templates.List().FirstOrDefault(t => t.Key == newProject.Template);
        var row = new ProjectProposalRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            PersonId = actor.PersonId,
            ConversationId = draft.ConversationId,
            Key = newProject.Key,
            Name = newProject.Name,
            Template = newProject.Template,
            TemplateName = template?.Name ?? newProject.Template,
            IncludeExamples = draft.IncludeExamples,
            Description = newProject.Description,
            Status = ProjectProposalRow.PendingStatus,
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (draft.ConversationId is { } conversationId)
        {
            // 一則回覆只能提一份：三條路（內建、本機、主機）都經過這裡，在對話的鎖裡檢查再存，同時來的呼叫不會一起通過。
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_project_proposal_reply:" + conversationId}, 0))", cancellationToken);
            if (await AlreadyProposedInThisReplyAsync(db, actor, conversationId, cancellationToken))
            {
                return new Failure(
                    FailureKind.Conflict, AiErrorCodes.ProjectProposalClosed,
                    $"這一則回覆已經提出過新專案建議了，一則回覆最多只能提出 {AiRules.MaxProjectProposalsPerReply} 份。請先請使用者看過，再請他在下一則訊息裡要你繼續。");
            }
        }

        db.ProjectProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ProjectProposalMapper.ToProposal(row);
    }

    // 「這一則回覆」= 對話裡最後一則使用者訊息之後；那之後已經有這個人在這則對話提的建議就算提過了。
    private static async Task<bool> AlreadyProposedInThisReplyAsync(
        AiDbContext db, Actor actor, Guid conversationId, CancellationToken cancellationToken)
    {
        var lastUserAt = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.OrganizationId == actor.OrganizationId && m.Role == "user")
            .OrderByDescending(m => m.Sequence)
            .Select(m => (DateTimeOffset?)m.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return await db.ProjectProposals.AsNoTracking().AnyAsync(
            p => p.ConversationId == conversationId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId
                && (lastUserAt == null || p.CreatedAt >= lastUserAt),
            cancellationToken);
    }

    private static bool HasControlCharacters(string? text, bool allowLineBreaks) =>
        text is not null && text.Any(c => char.IsControl(c) && !(allowLineBreaks && c is '\n' or '\r' or '\t'));
}
