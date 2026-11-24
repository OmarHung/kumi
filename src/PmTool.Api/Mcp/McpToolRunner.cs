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

    // 和同意頁的說法一樣（internal/HANDOFF-mcp.md 6.1），使用者在兩個地方看到的是同一組詞。
    private static readonly IReadOnlyDictionary<AccessTokenScope, string> ScopeLabels = new Dictionary<AccessTokenScope, string>
    {
        [AccessTokenScope.ItemsRead] = "看專案與項目",
        [AccessTokenScope.ItemsWrite] = "建立與修改項目、留言",
        [AccessTokenScope.BlueprintRead] = "看專案的設定與變更集",
        [AccessTokenScope.BlueprintWrite] = "提出變更集",
        [AccessTokenScope.AgentRun] = "當本機代理（只給 kumi agent start 的權杖）",
    };

    /// <summary>只列權杖的範圍涵蓋的工具：沒有 <c>agent:run</c> 的權杖看不到本機代理專用的工具。</summary>
    public ValueTask<ListToolsResult> ListAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        var http = httpContexts.HttpContext;
        var token = http is null ? null : SessionPrincipal.TryReadAccessToken(http.User);

        // 主機代理的工作只看得到授權的工具（工作建立時定的清單），不看範圍。
        var grant = http is null ? null : HostCredentials.TryGetGrant(http);
        var visible = McpToolCatalog.All.Where(tool =>
            grant is not null ? grant.AllowedTools.Contains(tool.Name) : token?.Scopes.Contains(tool.Scope) == true);
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
        return Render(await RunAsync(tool, context, request.Params?.Arguments, cancellationToken));
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
        return Render(HostProposalTools.IsProposal(tool.Name)
            ? await RunOnceAsync(tool, context, grant, http.RequestServices.GetRequiredService<IHostToolGrants>(), arguments, cancellationToken)
            : await RunAsync(tool, context, arguments, cancellationToken));
    }

    private async Task<McpToolResult> RunOnceAsync(
        McpTool tool,
        McpToolContext context,
        HostToolGrant grant,
        IHostToolGrants grants,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken)
    {
        var hash = McpArgumentHash.Of(arguments);
        var replay = await grants.BeginWriteAsync(grant.JobId, tool.Name, hash, cancellationToken);
        switch (replay.Kind)
        {
            case HostToolReplayKind.Replay when replay.ResultJson is { } stored:
                return new McpToolResult(IsError: false, stored);
            case HostToolReplayKind.Proceed:
                break;
            default:
                return McpToolResult.Error("同樣的提案正在建立，請稍候。");
        }

        string? resultJson = null;
        try
        {
            var result = await RunAsync(tool, context, arguments, cancellationToken);
            resultJson = result.IsError ? null : result.Json;
            return result;
        }
        finally
        {
            // 取消了也要放掉「進行中」：不然模型修正後再試會永遠看到「正在建立」。
            await grants.CompleteWriteAsync(grant.JobId, tool.Name, hash, resultJson, CancellationToken.None);
        }
    }

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
