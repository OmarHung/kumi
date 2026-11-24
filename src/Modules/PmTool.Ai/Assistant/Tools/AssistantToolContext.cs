using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 一則回覆裡的工具共用的東西。權限一律用發起的人的 <see cref="Actor"/>：助理不會因為是 AI 就多拿到什麼。
/// </summary>
internal sealed record AssistantToolContext(
    Actor Actor,
    string ProjectKey,
    IProjectService Projects,
    IBlueprintChangeService Changes,
    IBlueprintUsageService Usage,
    ITemplateLibrary Templates,
    IWorkItemService Items,
    IItemActivityService Activity,
    IPeopleDirectory People,
    IItemProposalCreator Proposals,
    Guid ConversationId,
    ILogger Logger);
