using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Actions.People;
using PmTool.Ai.Attachments;
using PmTool.Ai.Persistence;
using PmTool.Storage;
using PmTool.Work;

namespace PmTool.Ai.Actions.Attachments;

/// <summary>
/// 附件建議（internal/HANDOFF-ai-coverage-2.md 4.2）：把這則對話裡使用者上傳的檔案附到項目或知識庫頁面（<c>attach</c>），
/// 或刪除項目的附件（<c>delete</c>，不能復原）。助理一律不直接寫：使用者按「套用」才以自己的身分走工作引擎與知識庫的附件服務。
/// attach 不冪等（每套用一次就多一份附件），所以靠通用服務的「建立中」避免重複。知識庫服務用到才向容器要。
/// </summary>
internal sealed class AttachmentActionHandler(
    IDbContextFactory<AiDbContext> contexts,
    IFileStore store,
    ILoggerFactory loggers,
    IWorkItemService items,
    IProjectService projects,
    IAttachmentService itemAttachments,
    IServiceProvider services) : IActionProposalHandler
{
    private readonly ConversationAttachmentReader _conversation = new(contexts, store, loggers.CreateLogger<ConversationAttachmentReader>());

    public string Kind => ActionKinds.Attachments;

    public async Task<Result<PreparedAction>> PrepareAsync(ActionContext context, JsonElement payload, CancellationToken cancellationToken)
    {
        var issues = new List<FailureDetail>();
        if (PayloadJson.Changes(payload, issues) is not { } changes)
        {
            return PayloadJson.Invalid("附件建議不合規定，沒有建立。", issues);
        }

        var planner = new AttachmentPlanner(context, _conversation, items, projects, itemAttachments, services);
        var planned = await planner.PlanAsync(changes, cancellationToken);
        if (planned.Failure is { } failure)
        {
            return failure;
        }

        var (steps, cards) = planned.Value;
        var title = steps.Count == 1 ? $"建議處理附件（{cards[0].Label}）" : $"建議處理附件（{steps.Count} 項）";
        return new PreparedAction(title, cards, AttachmentStep.Write(steps), []);
    }

    public async Task<ActionStepResult> ApplyStepAsync(
        ActionContext context, JsonElement state, int index, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken)
    {
        var step = AttachmentStep.Read(state, index);
        return step.Action switch
        {
            AttachmentStep.Attach => await AttachAsync(context, step, cancellationToken),
            AttachmentStep.Delete => await DeleteAsync(context, step, cancellationToken),
            _ => throw new InvalidOperationException($"不認得的附件步驟：{step.Action}。"),
        };
    }

    private async Task<ActionStepResult> AttachAsync(ActionContext context, AttachmentStep step, CancellationToken cancellationToken)
    {
        // 套用時再讀一次：只認這則對話裡自己的檔案，檔案在建議之後被清掉也是「找不到」，不是例外。
        var opened = await _conversation.OpenAsync(context.Actor, context.ConversationId, step.AttachmentId, cancellationToken);
        if (opened.Failure is { } missing)
        {
            return ActionStepResult.Failed(new Failure(FailureKind.NotFound, missing.Code, "找不到這個檔案。"));
        }

        await using var content = opened.Value;
        var attachment = content.Attachment;
        if (step.ItemKey is { } itemKey)
        {
            var added = await itemAttachments.AddAsync(
                context.Actor,
                itemKey,
                new NewAttachment { FileName = attachment.FileName, ContentType = attachment.ContentType, Content = content.Content },
                cancellationToken);
            return added.Failure is { } failure
                ? ActionStepResult.Failed(failure)
                : ActionStepResult.Done($"/projects/{step.ProjectKey}/items/{itemKey}");
        }

        var pages = services.GetRequiredService<PmTool.Knowledge.IKnowledgeAttachmentService>();
        var addedToPage = await pages.AddAsync(
            context.Actor,
            step.SpaceKey!,
            step.PageNumber!.Value,
            new PmTool.Knowledge.NewPageAttachment { FileName = attachment.FileName, ContentType = attachment.ContentType, Content = content.Content },
            cancellationToken);
        return addedToPage.Failure is { } pageFailure
            ? ActionStepResult.Failed(pageFailure)
            : ActionStepResult.Done($"/knowledge/{step.SpaceKey}/{step.PageNumber}");
    }

    private async Task<ActionStepResult> DeleteAsync(ActionContext context, AttachmentStep step, CancellationToken cancellationToken)
    {
        // 服務只認附件編號；先確認它還在建議的那個項目上，不讓一份舊建議刪到別的東西。
        var listed = await itemAttachments.ListAsync(context.Actor, step.ItemKey!, cancellationToken);
        if (listed.Failure is { } notVisible)
        {
            return ActionStepResult.Failed(notVisible);
        }

        if (listed.Value.All(a => a.Id != step.AttachmentId))
        {
            return ActionStepResult.Failed(new Failure(
                FailureKind.NotFound, AiErrorCodes.AttachmentNotFound, $"{step.ItemKey} 已經沒有「{step.FileName}」這個附件了。"));
        }

        var deleted = await itemAttachments.DeleteAsync(context.Actor, step.AttachmentId, cancellationToken);
        return deleted.Failure is { } failure
            ? ActionStepResult.Failed(failure)
            : ActionStepResult.Done($"/projects/{step.ProjectKey}/items/{step.ItemKey}");
    }
}
