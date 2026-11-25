using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PmTool.Ai;
using PmTool.Api.OAuth;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Mcp;

/// <summary>
/// 列出工具與執行工具。每次呼叫都依序檢查：工具存在、權杖有那個範圍、沒超過每分鐘的次數、參數合格；
/// 之後的權限由模組用授權的人的身分判斷。預期中的失敗（範圍不夠、參數不對、模組擋下）都是工具的錯誤
/// （<c>isError: true</c>），不是協定的錯誤：代理看得到訊息，才會修正或告訴使用者。
/// </summary>
internal sealed class McpToolRunner(
    IHttpContextAccessor httpContexts,
    McpToolRateLimiter limiter,
    OAuthServerResolver resolver,
    ILogger<McpToolRunner> logger)
{
    private const string UnexpectedError = "工具執行時出錯了，請稍後再試。";

    // 提出類的寫入不跟著請求取消，但不能無限等：資料庫卡住時最多等這麼久。
    private static readonly TimeSpan ProposalWriteLimit = TimeSpan.FromSeconds(60);

    // 和同意頁的說法一樣（internal/HANDOFF-mcp.md 6.1），使用者在兩個地方看到的是同一組詞。
    private static readonly IReadOnlyDictionary<AccessTokenScope, string> ScopeLabels = new Dictionary<AccessTokenScope, string>
    {
        [AccessTokenScope.ItemsRead] = "看專案與項目",
        [AccessTokenScope.ItemsWrite] = "建立與修改項目、留言",
        [AccessTokenScope.BlueprintRead] = "看專案的設定與變更集",
        [AccessTokenScope.BlueprintWrite] = "提出變更集",
        [AccessTokenScope.AgentRun] = "當本機代理（只給 kumi agent start 的權杖）",
    };

    /// <summary>
    /// 只列權杖的範圍涵蓋的工具：沒有 <c>agent:run</c> 的權杖看不到本機代理專用的工具；
    /// 有 <c>agent:run</c> 的權杖再和本機代理的工具清單（<see cref="LocalAgentTools.Allowed"/>）取交集。
    /// </summary>
    public ValueTask<ListToolsResult> ListAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        var http = httpContexts.HttpContext;
        var token = http is null ? null : SessionPrincipal.TryReadAccessToken(http.User);

        // 主機代理的工作只看得到授權的工具（工作建立時定的清單），不看範圍。
        var grant = http is null ? null : HostCredentials.TryGetGrant(http);
        var visible = McpToolCatalog.All.Where(tool =>
            grant is not null ? grant.AllowedTools.Contains(tool.Name) : IsVisibleToToken(token, tool));
        return ValueTask.FromResult(new ListToolsResult { Tools = [.. visible.Select(Describe)] });
    }

    public async ValueTask<CallToolResult> CallAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var tool = McpToolCatalog.Find(request.Params?.Name)
            ?? throw new McpProtocolException($"沒有這個工具：{request.Params?.Name}。", McpErrorCode.InvalidParams);
        var http = httpContexts.HttpContext ?? throw new InvalidOperationException("MCP 的工具呼叫不在 HTTP 請求裡。");

        if (HostCredentials.TryGetGrant(http) is { } grant)
        {
            return await CallForGrantAsync(tool, grant, http, request.Params?.Arguments, cancellationToken);
        }

        // 入口已經擋掉沒有權杖的請求（McpRequestGuard）；走到這裡卻讀不到，是設定的漏洞，不默默當成匿名。
        var token = SessionPrincipal.TryReadAccessToken(http.User) ?? throw new InvalidOperationException("工具呼叫沒有存取權杖。");
        var user = SessionPrincipal.Read(http.User);

        if (!token.Scopes.Contains(tool.Scope))
        {
            return Render(McpToolResult.Error(MissingScopeMessage(tool.Scope)));
        }

        // 本機代理的權杖會交給成員電腦上的引擎：工具的上限由伺服器定，就算權杖另外有那個工具的範圍。
        if (McpProjectScope.Applies(token) && !LocalAgentTools.IsAllowed(tool.Name))
        {
            return Render(McpToolResult.Error($"本機代理的權杖不能用 {tool.Name} 這個工具。"));
        }

        if (!limiter.TryAcquire(user.PersonId))
        {
            return Render(McpToolResult.Error($"工具呼叫太頻繁了：每個人每分鐘最多 {McpLimits.ToolCallsPerMinute} 次，請稍後再試。"));
        }

        string? scope = null;
        if (McpProjectScope.Applies(token))
        {
            scope = McpProjectScope.Read(http.Request);
            if (scope is null)
            {
                return Render(McpToolResult.Error(McpProjectScope.MissingMessage));
            }
        }

        var context = new McpToolContext(user.ToActor(), resolver.Resolve(http.Request).Issuer, new McpServices(http.RequestServices))
        {
            ScopedProject = scope,
        };
        if (scope is null)
        {
            return Render(await RunAsync(tool, context, request.Params?.Arguments, cancellationToken));
        }

        return Render(await CallForLocalAgentAsync(tool, context, scope, http, request.Params?.Arguments, cancellationToken));
    }

    /// <summary>
    /// 本機代理（<c>agent:run</c>）的工具呼叫：每次先在伺服器端計這件工作的額度（<see cref="IAgentToolBudget"/>），
    /// 工作編號來自 <c>X-Kumi-Job</c>（kumi 寫進引擎的 MCP 設定，模型改不了）。提出類的工具沒有成功時退還提出的名額。
    /// </summary>
    private async Task<McpToolResult> CallForLocalAgentAsync(
        McpTool tool, McpToolContext context, string projectKey, HttpContext http, IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken)
    {
        var jobId = McpProjectScope.ReadJob(http.Request);
        if (jobId is null)
        {
            return McpToolResult.Error(
                http.Request.Headers.ContainsKey(McpProjectScope.JobHeader) ? McpProjectScope.UnknownJobMessage : McpProjectScope.MissingJobMessage);
        }

        var budget = http.RequestServices.GetRequiredService<IAgentToolBudget>();
        var isProposal = HostProposalTools.IsProposal(tool.Name);
        var outcome = await budget.UseLocalAsync(context.Actor, jobId.Value, projectKey, isProposal, cancellationToken);
        if (BudgetRefusal(outcome) is { } refusal)
        {
            return refusal;
        }

        var ticket = new BudgetTicket(budget, AgentJobKind.Local, jobId.Value, isProposal);
        return isProposal
            ? await RunProposalAsync(tool, context, arguments, ticket)
            : await RunAsync(tool, context, arguments, cancellationToken);
    }

    private static McpToolResult? BudgetRefusal(AgentToolBudgetOutcome outcome) => outcome switch
    {
        AgentToolBudgetOutcome.Allowed => null,
        AgentToolBudgetOutcome.ToolCallsExhausted =>
            McpToolResult.Error($"這件工作的工具呼叫已經用完了（每件工作最多 {AiRules.MaxAgentToolCallsPerJob} 次），請用手上已有的資料回答。"),
        AgentToolBudgetOutcome.ProposalsExhausted =>
            McpToolResult.Error($"這件工作提出的建議已經達到上限（每件工作最多 {AiRules.MaxAgentProposalsPerJob} 份），請先請使用者處理，不要再提出。"),
        _ => McpToolResult.Error(McpProjectScope.UnknownJobMessage),
    };

    /// <summary>
    /// 提出類的工具（本機代理）：寫入一旦成功就不能被請求的取消影響。SDK 傳進來的是請求的取消權杖（無狀態的 HTTP 模式下，
    /// 用戶端斷線就取消），寫入成功後才被取消會讓名額被誤退、結果也丟了；所以這一步不跟著請求取消，只受一個固定的上限約束。
    /// 沒有成功（工具的錯誤、例外）退還提出的名額。
    /// </summary>
    private async Task<McpToolResult> RunProposalAsync(
        McpTool tool, McpToolContext context, IDictionary<string, JsonElement>? arguments, BudgetTicket ticket)
    {
        using var bound = new CancellationTokenSource(ProposalWriteLimit);
        McpToolResult? result = null;
        try
        {
            result = await RunAsync(tool, context, arguments, bound.Token);
            return result;
        }
        finally
        {
            if (result is not { IsError: false })
            {
                await ticket.RefundAsync();
            }
        }
    }

    /// <summary>
    /// 主機代理的工作（<c>kjt_</c> 授權）呼叫工具：工具要在授權的清單裡；身分是發問的人（<see cref="HostToolGrant.ToActor"/>），
    /// 專案是工作的專案（模型改不了）。提出類的工具同樣的參數在一件工作裡只建立一次。
    /// </summary>
    private async Task<CallToolResult> CallForGrantAsync(
        McpTool tool, HostToolGrant grant, HttpContext http, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        if (!grant.AllowedTools.Contains(tool.Name))
        {
            return Render(McpToolResult.Error($"這件工作不能用 {tool.Name} 這個工具。"));
        }

        if (!limiter.TryAcquire(grant.PersonId))
        {
            return Render(McpToolResult.Error($"工具呼叫太頻繁了：每個人每分鐘最多 {McpLimits.ToolCallsPerMinute} 次，請稍後再試。"));
        }

        var context = new McpToolContext(grant.ToActor(), resolver.Resolve(http.Request).Issuer, new McpServices(http.RequestServices))
        {
            ScopedProject = grant.ProjectKey,
        };
        var budget = http.RequestServices.GetRequiredService<IAgentToolBudget>();
        var isProposal = HostProposalTools.IsProposal(tool.Name);
        if (BudgetRefusal(await budget.UseHostAsync(grant.JobId, isProposal, cancellationToken)) is { } refusal)
        {
            return Render(refusal);
        }

        if (!isProposal)
        {
            return Render(await RunAsync(tool, context, arguments, cancellationToken));
        }

        var ticket = new BudgetTicket(budget, AgentJobKind.Host, grant.JobId, IsProposal: true);
        return Render(await RunOnceAsync(tool, context, grant, http.RequestServices.GetRequiredService<IHostToolGrants>(), arguments, ticket));
    }

    private async Task<McpToolResult> RunOnceAsync(
        McpTool tool,
        McpToolContext context,
        HostToolGrant grant,
        IHostToolGrants grants,
        IDictionary<string, JsonElement>? arguments,
        BudgetTicket ticket)
    {
        var hash = McpArgumentHash.Of(arguments);

        // 開始與結束都不跟著請求取消（見 RunProposalAsync）：寫入成功之後一定要把結果記下來，重送才會拿到同一份而不是再建一份。
        using var bound = new CancellationTokenSource(ProposalWriteLimit);
        HostToolReplay replay;
        try
        {
            replay = await grants.BeginWriteAsync(grant.JobId, tool.Name, hash, bound.Token);
        }
        catch
        {
            // 還沒開始寫就失敗（資料庫斷線、逾時）：這次沒有提出任何東西，名額要還，模型重試才有名額（審查 L-11）。
            await ticket.RefundAsync();
            throw;
        }

        switch (replay.Kind)
        {
            case HostToolReplayKind.Replay when replay.ResultJson is { } stored:
                // 重播已經建立的那一份，沒有新增提案：名額退還。
                await ticket.RefundAsync();
                return new McpToolResult(IsError: false, stored);
            case HostToolReplayKind.Proceed:
                break;
            default:
                await ticket.RefundAsync();
                return McpToolResult.Error("同樣的提案正在建立，請稍候。");
        }

        string? resultJson = null;
        try
        {
            var result = await RunAsync(tool, context, arguments, bound.Token);
            resultJson = result.IsError ? null : result.Json;
            return result;
        }
        finally
        {
            // 寫入成功就立刻記下；沒有成功（錯誤、例外）放掉「進行中」並退還名額，模型修正後才能再試。
            await grants.CompleteWriteAsync(grant.JobId, tool.Name, hash, resultJson, CancellationToken.None);
            if (resultJson is null)
            {
                await ticket.RefundAsync();
            }
        }
    }

    private sealed record BudgetTicket(IAgentToolBudget Budget, AgentJobKind Kind, Guid JobId, bool IsProposal)
    {
        public Task RefundAsync() => IsProposal ? Budget.RefundProposalAsync(Kind, JobId) : Task.CompletedTask;
    }

    private static bool IsVisibleToToken((Guid TokenId, IReadOnlyList<AccessTokenScope> Scopes)? token, McpTool tool) =>
        token?.Scopes.Contains(tool.Scope) == true && (!McpProjectScope.Applies(token) || LocalAgentTools.IsAllowed(tool.Name));

    public static string MissingScopeMessage(AccessTokenScope scope) =>
        $"這個連線沒有「{ScopeLabels[scope]}」（{AccessTokenScopes.NameOf(scope)}）的權限。" +
        "請到 Kumi 的「我的帳號」撤銷這個授權，重新連線時勾選這一項；如果用的是個人存取權杖，請建立有這個範圍的新權杖。";

    private async Task<McpToolResult> RunAsync(
        McpTool tool, McpToolContext context, IDictionary<string, JsonElement>? raw, CancellationToken cancellationToken)
    {
        try
        {
            var arguments = new McpArguments(raw);
            if (arguments.Names.FirstOrDefault(name => !tool.ParameterNames.Contains(name)) is { } unknown)
            {
                return McpToolResult.Error($"{tool.Name} 沒有參數 {unknown}。可用的參數：{string.Join("、", tool.ParameterNames)}。");
            }

            return await tool.Run(context, arguments, cancellationToken);
        }
        catch (McpArgumentException exception)
        {
            return McpToolResult.Error(exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 不把例外的訊息交給代理：可能有連線字串或其他內部資訊，它也用不到。
            logger.LogWarning(exception, "MCP 工具 {Tool} 執行時出錯。", tool.Name);
            return McpToolResult.Error(UnexpectedError);
        }
    }

    private static CallToolResult Render(McpToolResult result) => new()
    {
        IsError = result.IsError,
        Content = [new TextContentBlock { Text = result.Json }],
    };

    private static Tool Describe(McpTool tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = tool.ParsedSchema(),
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = tool.ReadOnly,
            DestructiveHint = false,
            OpenWorldHint = false,
        },
    };
}
