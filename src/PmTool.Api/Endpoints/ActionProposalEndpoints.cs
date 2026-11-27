using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Responses;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的通用動作建議（知識庫、人員、附件、存的檢視、說明格式）：讀、套用、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 套用時 body 可以帶卡片上使用者填的值（<c>{"inputs": {"password:0": "…"}}</c>），只交給服務、不記錄、不放進回應。
/// 規格見 <c>internal/HANDOFF-ai-coverage-2.md</c> 第 4.1 節。
/// </summary>
internal static class ActionProposalEndpoints
{
    private const string ProposalRoute = "/ai/action-proposals/{proposalId:guid}";
    private const int MaxInputCount = 20;
    private const int MaxInputKeyLength = 100;
    private const int MaxInputValueLength = 1000;
    private const int MaxBodyBytes = 64 * 1024;

    public static RouteGroupBuilder MapActionProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetActionProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyActionProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissActionProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IActionProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 套用不跟著請求取消：做到一半、使用者關掉頁面，已經寫入的與建議的狀態都要存下來（模組自己也不跟著取消，這裡是第二道）。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, HttpRequest request, ClaimsPrincipal principal, [FromServices] IActionProposalService proposals)
    {
        var inputs = await ReadInputsAsync(request, request.HttpContext.RequestAborted);
        if (inputs.Response is { } tooLarge)
        {
            return tooLarge.ToResult();
        }

        return inputs.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : Render(await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, inputs.Inputs, CancellationToken.None));
    }

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IActionProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(Result<ActionProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(ActionProposalData.From(result.Value));

    // 沒有 body、或沒有 inputs 都當作沒填；格式不對一律 422，訊息不回顯收到的值。
    private static async Task<InputsResult> ReadInputsAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is 0 or null && !request.Headers.ContainsKey("Transfer-Encoding"))
        {
            return new InputsResult(NoInputs, null);
        }

        if (request.ContentLength > MaxBodyBytes)
        {
            return TooLarge();
        }

        try
        {
            // 分塊傳輸沒有 Content-Length，所以不管有沒有都包一層：讀進來超過上限就停。
            using var document = await JsonDocument.ParseAsync(new LimitedBodyStream(request.Body, MaxBodyBytes), cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new InputsResult(NoInputs, Invalid("body 要是一個物件。"));
            }

            if (!document.RootElement.TryGetProperty("inputs", out var element) || element.ValueKind == JsonValueKind.Null)
            {
                return new InputsResult(NoInputs, null);
            }

            return ParseInputs(element);
        }
        catch (AttachmentUpload.RequestTooLargeException)
        {
            return TooLarge();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }
        catch (JsonException)
        {
            return new InputsResult(NoInputs, Invalid("body 不是有效的 JSON。"));
        }
    }

    private static InputsResult TooLarge() => new(NoInputs, null, ApiFailure.PayloadLargerThan(MaxBodyBytes));

    private static InputsResult ParseInputs(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new InputsResult(NoInputs, Invalid("inputs 要是一個物件。"));
        }

        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            if (value is null || inputs.Count >= MaxInputCount || !IsClean(property.Name, MaxInputKeyLength) || !IsClean(value, MaxInputValueLength))
            {
                return new InputsResult(NoInputs, Invalid("inputs 的 key 與值要是不含控制字元的短字串。"));
            }

            inputs[property.Name] = value;
        }

        return new InputsResult(inputs, null);
    }

    private static readonly IReadOnlyDictionary<string, string> NoInputs = new Dictionary<string, string>();

    private static bool IsClean(string text, int maxLength) => text.Length <= maxLength && !text.Any(char.IsControl);

    private static Failure Invalid(string message) => new(FailureKind.Invalid, AiErrorCodes.FieldInvalid, message);
}

/// <summary>
/// 建議的形狀（交接 4.1）。<c>status</c> 是 pending、applying、applied、dismissed；步驟的 <c>format</c> 是 text 或 markdown；
/// 輸入的 <c>kind</c> 是 password 或 text。沒有的值寫出 null，沒有輸入是 <c>[]</c>。
/// </summary>
internal sealed record ActionProposalData(
    Guid Id,
    string Kind,
    string? ProjectKey,
    string Title,
    string? Reason,
    IReadOnlyList<ActionStepData> Steps,
    IReadOnlyList<ActionInputData> Inputs,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? Error)
{
    public static ActionProposalData From(ActionProposal proposal) => new(
        proposal.Id,
        proposal.Kind,
        proposal.ProjectKey,
        proposal.Title,
        proposal.Reason,
        [.. proposal.Steps.Select(step => new ActionStepData(
            step.Label, step.Target, step.Before, step.After, step.Note, FormatName(step.Format), step.Done, step.Error, step.Link))],
        [.. proposal.Inputs.Select(input => new ActionInputData(input.Key, input.Label, KindName(input.Kind), input.Hint))],
        StatusName(proposal.Status),
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.Error);

    private static string StatusName(NewItemProposalStatus status) => status switch
    {
        NewItemProposalStatus.Pending => "pending",
        NewItemProposalStatus.Applying => "applying",
        NewItemProposalStatus.Applied => "applied",
        NewItemProposalStatus.Dismissed => "dismissed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "不認得的建議狀態。"),
    };

    private static string FormatName(ActionTextFormat format) => format == ActionTextFormat.Markdown ? "markdown" : "text";

    private static string KindName(ActionInputKind kind) => kind == ActionInputKind.Password ? "password" : "text";
}

internal sealed record ActionStepData(
    string Label, string? Target, string? Before, string? After, string? Note, string Format, bool Done, string? Error, string? Link);

internal sealed record ActionInputData(string Key, string Label, string Kind, string? Hint);

internal sealed record InputsResult(IReadOnlyDictionary<string, string> Inputs, Failure? Failure, ApiFailure? Response = null);
