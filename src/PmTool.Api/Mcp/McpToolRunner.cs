using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PmTool.Abstractions;
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
    /// 有 <c>agent:run</c> 的權杖再和這次請求的範圍能用的工具取交集：專案的對話是 <see cref="LocalAgentTools.Allowed"/>，
    /// 不選專案的組織對話是 <see cref="LocalAgentTools.AllowedForOrganization"/>。
    /// </summary>
    public async ValueTask<ListToolsResult> ListAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        var http = httpContexts.HttpContext;
        var token = http is null ? null : SessionPrincipal.TryReadAccessToken(http.User);

        // 主機代理的工作只看得到授權的工具（工作建立時定的清單），不看範圍；不選專案的工作再和組織對話的清單取交集。
        var grant = http is null ? null : HostCredentials.TryGetGrant(http);
        var isAgent = grant is null && http is not null && McpProjectScope.Applies(token);
        var agentScope = isAgent ? await ResolveListScopeAsync(http!, cancellationToken) : null;
        if (isAgent && agentScope is null)
        {
            // 找不到工作：不知道這是哪一則對話，什麼都不列（呼叫時也會被擋）。
            return new ListToolsResult { Tools = [] };
        }

        var visible = McpToolCatalog.All.Where(tool => grant is not null
            ? GrantAllows(grant, tool.Name)
            : IsVisibleToToken(token, tool) && (agentScope is not { } scope || scope.AllowsTool(tool.Name)));
        return new ListToolsResult { Tools = [.. visible.Select(Describe)] };
    }

    /// <summary>
    /// 列工具時的範圍：有工作標頭就和呼叫時一樣用工作找到對話核對專案標頭（對不上是 null，什麼都不列）；
    /// 沒有工作標頭（舊版 kumi）只有專案標頭時，列專案的清單，呼叫時會被要求更新。找不到工作是 null。
    /// </summary>
    private async Task<McpScope?> ResolveListScopeAsync(HttpContext http, CancellationToken cancellationToken)
    {
        if (!McpProjectScope.HasJobHeader(http.Request) && McpProjectScope.Read(http.Request) is { } key)
        {
            return McpScope.ForProject(key);
        }

        var actor = SessionPrincipal.Read(http.User).ToActor();
        return (await ResolveLocalScopeAsync(http, actor, cancellationToken)).Scope;
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
        // 這裡先擋兩種清單都沒有的；範圍確定之後（專案或組織）再用那一種的清單擋一次。
        var isAgent = McpProjectScope.Applies(token);
        if (isAgent && !LocalAgentTools.IsAllowed(tool.Name) && !LocalAgentTools.IsAllowedForOrganization(tool.Name))
        {
            return Render(McpToolResult.Error(ToolNotAllowedMessage(tool.Name)));
        }

        if (!limiter.TryAcquire(user.PersonId))
        {
            return Render(McpToolResult.Error(RateLimitedMessage));
        }

        var issuer = resolver.Resolve(http.Request).Issuer;
        if (!isAgent)
        {
            var ordinary = new McpToolContext(user.ToActor(), issuer, new McpServices(http.RequestServices)) { Scope = McpScope.Unrestricted };
            return Render(await RunAsync(tool, ordinary, request.Params?.Arguments, cancellationToken));
        }

        return Render(await CallAsAgentAsync(tool, user.ToActor(), issuer, http, request.Params?.Arguments, cancellationToken));
    }

    /// <summary>本機代理的呼叫：先用工作決定範圍，再用那個範圍的清單擋一次，然後才計額度與執行。</summary>
    private async Task<McpToolResult> CallAsAgentAsync(
        McpTool tool, Actor actor, string issuer, HttpContext http, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        var resolution = await ResolveLocalScopeAsync(http, actor, cancellationToken);
        if (resolution.Refusal is { } refusal)
        {
            return refusal;
        }

        var scope = resolution.Scope!.Value;
        if (!scope.AllowsTool(tool.Name))
        {
            return McpToolResult.Error(ToolNotAllowedMessage(tool.Name));
        }

        var context = new McpToolContext(actor, issuer, new McpServices(http.RequestServices))
        {
            Scope = scope,
            ConversationId = resolution.ConversationId,
        };
        return await CallForLocalAgentAsync(tool, context, resolution.JobId, http, arguments, cancellationToken);
    }

    private static readonly string RateLimitedMessage = $"工具呼叫太頻繁了：每個人每分鐘最多 {McpLimits.ToolCallsPerMinute} 次，請稍後再試。";

    private static string ToolNotAllowedMessage(string toolName) => $"本機代理的權杖不能用 {toolName} 這個工具。";

    private sealed record LocalScopeResolution(McpScope? Scope, Guid JobId, Guid? ConversationId, McpToolResult? Refusal);

    /// <summary>
    /// 本機代理這個請求的範圍：用 <c>X-Kumi-Job</c> 的工作找到對話（伺服器端，模型改不了），對話有專案是限定專案（<c>X-Kumi-Project</c>
    /// 一定要帶而且相同），沒有專案是只限組織（<c>X-Kumi-Project</c> 一定不能帶）。找不到工作、不是這個人的、已經結束的一律是同一句「找不到這件工作」。
    /// </summary>
    private async Task<LocalScopeResolution> ResolveLocalScopeAsync(HttpContext http, Actor actor, CancellationToken cancellationToken)
    {
        var jobId = McpProjectScope.ReadJob(http.Request);
        if (jobId is null)
        {
            return Refused(McpProjectScope.HasJobHeader(http.Request) ? McpProjectScope.UnknownJobMessage : McpProjectScope.MissingJobMessage);
        }

        var budget = http.RequestServices.GetRequiredService<IAgentToolBudget>();
        if (await budget.FindJobScopeAsync(AgentJobKind.Local, actor, jobId.Value, cancellationToken) is not { } found)
        {
            return Refused(McpProjectScope.UnknownJobMessage);
        }

        var header = McpProjectScope.Read(http.Request);
        if (found.ProjectKey is null)
        {
            return McpProjectScope.HasHeader(http.Request)
                ? Refused(McpProjectScope.OrganizationJobWithProjectMessage)
                : new LocalScopeResolution(McpScope.OrganizationOnly, jobId.Value, found.ConversationId, null);
        }

        return string.Equals(header, found.ProjectKey, StringComparison.OrdinalIgnoreCase)
            ? new LocalScopeResolution(McpScope.ForProject(found.ProjectKey), jobId.Value, found.ConversationId, null)
            : Refused(McpProjectScope.UnknownJobMessage);

        static LocalScopeResolution Refused(string message) => new(null, Guid.Empty, null, McpToolResult.Error(message));
    }

    /// <summary>
    /// 本機代理（<c>agent:run</c>）的工具呼叫：每次先在伺服器端計這件工作的額度（<see cref="IAgentToolBudget"/>），
    /// 工作編號來自 <c>X-Kumi-Job</c>（kumi 寫進引擎的 MCP 設定，模型改不了）。提出類的工具沒有成功時退還提出的名額。
    /// 不選專案的對話，專案代號傳 null。
    /// </summary>
    private async Task<McpToolResult> CallForLocalAgentAsync(
        McpTool tool, McpToolContext context, Guid jobId, HttpContext http, IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken)
    {
        var budget = http.RequestServices.GetRequiredService<IAgentToolBudget>();
        var isProposal = HostProposalTools.IsProposal(tool.Name);
        var outcome = await budget.UseLocalAsync(context.Actor, jobId, context.Scope.ProjectKey, isProposal, cancellationToken);
        if (BudgetRefusal(outcome) is { } refusal)
        {
            return refusal;
        }

        var ticket = new BudgetTicket(budget, AgentJobKind.Local, jobId, isProposal);
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
        if (!GrantAllows(grant, tool.Name))
        {
            return Render(McpToolResult.Error($"這件工作不能用 {tool.Name} 這個工具。"));
        }

        if (!limiter.TryAcquire(grant.PersonId))
        {
            return Render(McpToolResult.Error(RateLimitedMessage));
        }

        // 授權的專案是 null 是不選專案的組織對話：只限組織，不是「不限」。
        var scope = McpScope.ForJob(grant.ProjectKey);
        var budget = http.RequestServices.GetRequiredService<IAgentToolBudget>();
        var isProposal = HostProposalTools.IsProposal(tool.Name);
        if (BudgetRefusal(await budget.UseHostAsync(grant.JobId, isProposal, cancellationToken)) is { } refusal)
        {
            return Render(refusal);
        }

        var context = new McpToolContext(grant.ToActor(), resolver.Resolve(http.Request).Issuer, new McpServices(http.RequestServices))
        {
            Scope = scope,
            ConversationId = isProposal && scope.Kind == McpScopeKind.OrganizationOnly
                ? (await budget.FindJobScopeAsync(AgentJobKind.Host, null, grant.JobId, cancellationToken))?.ConversationId
                : null,
        };
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

    // 範圍之外的限制（專案或組織的清單）由呼叫的人再加上：這裡只看權杖的範圍涵蓋它，而且是本機代理能用的工具之一。
    private static bool IsVisibleToToken((Guid TokenId, IReadOnlyList<AccessTokenScope> Scopes)? token, McpTool tool) =>
        token?.Scopes.Contains(tool.Scope) == true
        && (!McpProjectScope.Applies(token) || LocalAgentTools.IsAllowed(tool.Name) || LocalAgentTools.IsAllowedForOrganization(tool.Name));

    /// <summary>工具在授權的清單裡；不選專案的授權（<see cref="HostToolGrant.ProjectKey"/> 是 null）再和組織對話的清單取交集。</summary>
    private static bool GrantAllows(HostToolGrant grant, string toolName) =>
        grant.AllowedTools.Contains(toolName) && (grant.ProjectKey is not null || LocalAgentTools.IsAllowedForOrganization(toolName));

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
