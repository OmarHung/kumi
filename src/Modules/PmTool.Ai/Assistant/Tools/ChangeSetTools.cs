using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Work;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>寫變更集的工具：先預覽（只算不存），再提出（存成等待核准，標明是 AI 助理提出的）。</summary>
internal abstract class ChangeSetTool(string name, string description, AssistantToolContext context) : AssistantTool(
    name,
    description,
    """
    {"type":"object","properties":{"changeset":{"type":"object","description":"變更集文件：baseVersion、summary、blueprint（改完之後的整份藍圖）、migrations。格式見系統提示裡的 changeset.schema.json。直接放 JSON 物件，不要包成字串。"}},"required":["changeset"],"additionalProperties":false}
    """,
    context)
{
    protected const string InvalidError = "變更集驗證不過";

    protected const string MissingChangeSetError = "缺少參數 changeset，它要是一個 JSON 物件。";

    protected static string? DocumentOf(AIFunctionArguments arguments) => ToolArguments.ObjectText(arguments, "changeset");

    /// <summary>驗證或計畫不過是預期中的結果：模型看得到每一個問題，通常會自己修正再試。</summary>
    protected static string FailureResult(Failure failure) =>
        failure.Code == WorkErrorCodes.ChangeSetInvalid
            ? ToolResults.ErrorWithIssues(InvalidError, failure.Details)
            : ToolResults.FromFailure(failure);
}

internal sealed class PreviewChangeSetTool(AssistantToolContext context) : ChangeSetTool(
    AssistantToolNames.PreviewChangeSet,
    "算一份變更集會發生什麼事，但不存：回傳差異（changes）、對既有資料的影響（impacts，affected 是會動到的項目數）、擋住套用的原因（blockers）。提出之前一定先呼叫。",
    context)
{
    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (DocumentOf(arguments) is not { } document)
        {
            return ToolResults.Error(MissingChangeSetError);
        }

        var result = await Context.Changes.PreviewAsync(
            Context.Actor, Context.ProjectKey, document, DocumentFormat.Json, cancellationToken);
        if (result.Failure is { } failure)
        {
            return FailureResult(failure);
        }

        var preview = result.Value;
        return ToolResults.Ok(new
        {
            baseVersion = preview.BaseVersion,
            currentVersion = preview.CurrentVersion,
            canApply = preview.CanApply,
            changes = preview.Changes.Select(ChangeSetShape.Change),
            impacts = preview.Impacts.Select(ChangeSetShape.Impact),
            blockers = preview.Blockers.Select(ChangeSetShape.Blocker),
        });
    }
}

internal sealed class ProposeChangeSetTool(AssistantToolContext context) : ChangeSetTool(
    AssistantToolNames.ProposeChangeSet,
    "提出一份變更集，等有權限的人在「變更集」頁核准才會生效。提出之前先用 preview_changeset 確認沒有問題。回傳變更集的編號與網址。",
    context)
{
    private const string NoPermissionError = "你沒有提出變更集的權限。";

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (DocumentOf(arguments) is not { } document)
        {
            return ToolResults.Error(MissingChangeSetError);
        }

        var result = await Context.Changes.ProposeAsync(
            Context.Actor, Context.ProjectKey, document, DocumentFormat.Json, ChangeSetOrigin.Assistant, cancellationToken);
        if (result.Failure is { } failure)
        {
            return failure.Kind == FailureKind.Forbidden && !await CanProposeAsync(cancellationToken)
                ? ToolResults.Error(NoPermissionError)
                : FailureResult(failure);
        }

        var detail = result.Value;
        return ToolResults.Ok(new
        {
            changeSet = new
            {
                number = detail.ChangeSet.Number,
                url = $"/projects/{Context.ProjectKey}/changesets/{detail.ChangeSet.Number}",
                canApply = detail.CanApply,
                impacts = detail.Impacts.Select(ChangeSetShape.Impact),
            },
        });
    }

    // 失敗是 Forbidden 時才多查一次：區分「沒有提出的權限」與其他被擋的原因（例如要看得到全部項目）。
    private async Task<bool> CanProposeAsync(CancellationToken cancellationToken)
    {
        var project = await Context.Projects.GetAsync(Context.Actor, Context.ProjectKey, cancellationToken);
        return project.Failure is null && project.Value.MyPermissions.Contains(WorkPermissions.BlueprintPropose);
    }
}
