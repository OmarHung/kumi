using PmTool.Abstractions;
using PmTool.Api.Endpoints;
using PmTool.Blueprint;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 變更集的工具。預覽與讀取要 <c>blueprint:read</c>，提出要 <c>blueprint:write</c>。沒有「套用」：AI 只提案，要有權限的人在網頁上核准。
/// </summary>
internal static class ChangeSetTools
{
    private const string ChangeSetDescription =
        "變更集文件：baseVersion、summary、blueprint（改完之後的整份藍圖）、migrations。格式見 schemas/blueprint/v0/changeset.schema.json。" +
        "直接放 JSON 物件，不要包成字串。baseVersion 是 get_blueprint 回的 version。";

    public static IEnumerable<McpTool> All { get; } =
    [
        new("preview_changeset",
            "算一份變更集會發生什麼事，但不存：回傳差異（changes）、對既有資料的影響（impacts，affected 是會動到的項目數）、擋住套用的原因（blockers）。" +
            "提出之前一定先呼叫。",
            McpSchemas.Object(
                required: ["projectKey", "changeset"],
                ("projectKey", McpSchemas.Text("專案代號，例如 WEB。")),
                ("changeset", McpSchemas.FreeObject(ChangeSetDescription))),
            AccessTokenScope.BlueprintRead, ReadOnly: true, PreviewAsync),
        new("get_changeset",
            "讀專案裡一份變更集：狀態、誰提出的、從哪裡提出的（origin）、差異、對資料的影響、現在能不能套用。",
            McpSchemas.Object(
                required: ["projectKey", "number"],
                ("projectKey", McpSchemas.Text("專案代號，例如 WEB。")),
                ("number", McpSchemas.Integer("變更集的編號（畫面上的 #3 就是 3）。"))),
            AccessTokenScope.BlueprintRead, ReadOnly: true, GetAsync),
        new("propose_changeset",
            "提出一份變更集，等有權限的人在 Kumi 的網頁核准才會生效；你不能套用它。提出之前先用 preview_changeset 確認沒有問題，" +
            "並先跟使用者確認內容。回傳變更集的編號與網頁的網址，請把網址交給使用者。",
            McpSchemas.Object(
                required: ["projectKey", "changeset"],
                ("projectKey", McpSchemas.Text("專案代號，例如 WEB。")),
                ("changeset", McpSchemas.FreeObject(ChangeSetDescription))),
            AccessTokenScope.BlueprintWrite, ReadOnly: false, ProposeAsync),
    ];

    private static async Task<McpToolResult> PreviewAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var result = await context.Services.Changes.PreviewAsync(
            context.Actor, context.Project(arguments.RequiredPlain("projectKey")), arguments.RequiredObjectText("changeset"), DocumentFormat.Json, cancellationToken);
        if (result.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var preview = result.Value;
        return McpToolResult.Ok(new
        {
            baseVersion = preview.BaseVersion,
            currentVersion = preview.CurrentVersion,
            canApply = preview.CanApply,
            changes = preview.Changes.Select(ChangeData.From),
            impacts = preview.Impacts.Select(ImpactData.FromPlanned),
            blockers = preview.Blockers.Select(blocker => new BlockerData(blocker.Code, blocker.Path, blocker.Message)),
        });
    }

    private static async Task<McpToolResult> GetAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var result = await context.Services.Changes.GetAsync(
            context.Actor, context.Project(arguments.RequiredPlain("projectKey")), arguments.RequiredLong("number"), cancellationToken);
        if (result.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var detail = await DetailOfAsync(context, result.Value, cancellationToken);
        return McpToolResult.Ok(new
        {
            notice = BlueprintReadTools.ContentNotice,
            detail.ChangeSet,
            detail.CanApply,
            detail.Changes,
            detail.Impacts,
            detail.Blockers,
        });
    }

    private static async Task<McpToolResult> ProposeAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        var result = await context.Services.Changes.ProposeAsync(
            context.Actor, projectKey, arguments.RequiredObjectText("changeset"), DocumentFormat.Json, ChangeSetOrigin.Agent, cancellationToken);
        if (result.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var detail = await DetailOfAsync(context, result.Value, cancellationToken);
        return McpToolResult.Ok(new
        {
            notice = BlueprintReadTools.ContentNotice,
            detail.ChangeSet,
            url = ChangeSetUrl(context.Issuer, projectKey, result.Value.ChangeSet.Number),
            detail.CanApply,
            detail.Changes,
            detail.Impacts,
            detail.Blockers,
        });
    }

    /// <summary>網頁上這份變更集的網址。issuer 是這個部署對外的網址（不從請求的 Host 標頭來）。</summary>
    public static string ChangeSetUrl(string issuer, string projectKey, long number) =>
        $"{issuer}/projects/{Uri.EscapeDataString(projectKey.Trim().ToUpperInvariant())}/changesets/{number}";

    // 不帶藍圖全文：差異與影響已經說明了改了什麼，全文要看用 get_blueprint；省得每次讀取都多幾萬字。
    private static async Task<ChangeSetView> DetailOfAsync(McpToolContext context, ChangeSetDetail detail, CancellationToken cancellationToken)
    {
        var names = await PeopleNames.LookupAsync(
            context.Services.People, context.Actor.OrganizationId, ChangeSetData.PeopleOf(detail.ChangeSet), cancellationToken);
        return new ChangeSetView(
            ChangeSetData.From(detail.ChangeSet, names),
            detail.CanApply,
            [.. detail.Changes.Select(ChangeData.From)],
            [.. detail.Impacts.Select(ImpactData.FromPlanned)],
            [.. detail.Blockers.Select(blocker => new BlockerData(blocker.Code, blocker.Path, blocker.Message))]);
    }

    private sealed record ChangeSetView(
        ChangeSetData ChangeSet,
        bool CanApply,
        IReadOnlyList<ChangeData> Changes,
        IReadOnlyList<ImpactData> Impacts,
        IReadOnlyList<BlockerData> Blockers);
}
