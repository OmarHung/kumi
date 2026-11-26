using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// 走遍主機所有的端點，依「誰能呼叫」分成四類，前三類的清單寫死在這裡：
/// 匿名、主機代理的機器憑證、只能用 Cookie（要求登入、沒有宣告任何權杖範圍）、其餘的都宣告了權杖的範圍。
/// 新加的端點忘了授權（變成匿名），或忘了宣告範圍（變成只能用 Cookie）時，這裡會停下來，要人確認那是故意的。
/// 「agent:run 與任何權杖都能打的」那份清單由 <see cref="AgentTokenEndpointListTests"/> 盯，這裡不重複。
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class EndpointInventoryTests
{
    // 型別是 internal，測試專案看不到：用型別名稱從端點的 metadata 找。
    private const string RequirementTypeName = "TokenScopeRequirement";
    private const string HostAgentTypeName = "HostAgentEndpoint";

    private static readonly string[] Anonymous =
    [
        "DELETE /mcp",
        "DELETE /v1/session",
        "GET /.well-known/oauth-authorization-server",
        "GET /.well-known/oauth-protected-resource",
        "GET /.well-known/oauth-protected-resource/mcp",
        "GET /health",
        "GET /health/ready",
        "GET /mcp",
        "POST /mcp/",
        "POST /oauth/register",
        "POST /oauth/revoke",
        "POST /oauth/token",
        "POST /v1/blueprints/validate",
        "POST /v1/changesets/validate",
        "POST /v1/cli-login/token",
        "POST /v1/session",
    ];

    private static readonly string[] MachineCredential =
    [
        "GET /v1/host-agent/jobs/{jobId:guid}/attachments/{attachmentId:guid}",
        "POST /v1/host-agent/check-in",
        "POST /v1/host-agent/jobs/{jobId:guid}/events",
        "POST /v1/host-agent/jobs/{jobId:guid}/finish",
        "POST /v1/host-agent/logins/claim",
        "POST /v1/host-agent/logins/{loginId:guid}/complete",
        "POST /v1/host-agent/pair",
    ];

    private static readonly string[] CookieOnly =
    [
        "(any) /v1/live",
        "(any) /v1/live/negotiate",
        "DELETE /v1/account/ai-connections/{connectionId:guid}",
        "DELETE /v1/account/local-agents/{machineId:guid}",
        "DELETE /v1/account/oauth-grants/{grantId}",
        "DELETE /v1/account/tokens/{tokenId}",
        "DELETE /v1/ai/connections/{connectionId:guid}",
        "DELETE /v1/ai/host/agents/{agentId:guid}",
        "DELETE /v1/ai/host/logins/{loginId:guid}",
        "DELETE /v1/ai/host/sources/{sourceId:guid}",
        "DELETE /v1/assistant/conversations/{conversationId:guid}",
        "DELETE /v1/description-templates/{id}",
        "DELETE /v1/knowledge/spaces/{spaceKey}/pages/{number}",
        "DELETE /v1/my-work/saved-views/{viewId:guid}",
        "DELETE /v1/projects/{projectKey}/assistant/attachments/{id}",
        "DELETE /v1/projects/{projectKey}/assistant/conversations/{conversationId:guid}",
        "DELETE /v1/projects/{projectKey}/members/{personId:guid}",
        "DELETE /v1/projects/{projectKey}/saved-views/{viewId:guid}",
        "GET /v1/account/ai-connections",
        "GET /v1/account/email-notifications",
        "GET /v1/account/local-agents",
        "GET /v1/account/oauth-grants",
        "GET /v1/account/tokens",
        "GET /v1/ai/host",
        "GET /v1/ai/host/sources/{sourceId:guid}/usage",
        "GET /v1/ai/item-proposals/{proposalId:guid}",
        "GET /v1/ai/member-proposals/{proposalId:guid}",
        "GET /v1/ai/new-item-proposals/{proposalId:guid}",
        "GET /v1/ai/options",
        "GET /v1/ai/project-proposals/{proposalId:guid}",
        "GET /v1/ai/settings",
        "GET /v1/assistant/conversations",
        "GET /v1/assistant/conversations/{conversationId:guid}",
        "GET /v1/audit-events",
        "GET /v1/email-settings",
        "GET /v1/knowledge/attachments/{id}/content",
        "GET /v1/knowledge/spaces/",
        "GET /v1/knowledge/spaces/{spaceKey}",
        "GET /v1/knowledge/spaces/{spaceKey}/pages/{number}",
        "GET /v1/knowledge/spaces/{spaceKey}/pages/{number}/attachments",
        "GET /v1/knowledge/spaces/{spaceKey}/pages/{number}/restriction",
        "GET /v1/knowledge/spaces/{spaceKey}/trash",
        "GET /v1/knowledge/spaces/{spaceKey}/tree",
        "GET /v1/my-work/saved-views",
        "GET /v1/projects/{projectKey}/assistant/attachments/{id}/content",
        "GET /v1/projects/{projectKey}/assistant/conversations",
        "GET /v1/projects/{projectKey}/assistant/conversations/{conversationId:guid}",
        "GET /v1/projects/{projectKey}/saved-views",
        "GET /v1/storage-usage",
        "PATCH /v1/ai/host/agents/{agentId:guid}",
        "PATCH /v1/knowledge/spaces/{spaceKey}/pages/{number}",
        "POST /v1/account/ai-connections",
        "POST /v1/account/ai-connections/{connectionId:guid}/refresh",
        "POST /v1/account/ai-models/discover",
        "POST /v1/account/other-sessions/sign-out",
        "POST /v1/account/tokens",
        "POST /v1/ai/connections",
        "POST /v1/ai/connections/{connectionId:guid}/refresh",
        "POST /v1/ai/host/agents",
        "POST /v1/ai/host/agents/{agentId:guid}/engines/{engine}/login",
        "POST /v1/ai/host/agents/{agentId:guid}/engines/{engine}/logout",
        "POST /v1/ai/host/agents/{agentId:guid}/pairing",
        "POST /v1/ai/host/sources",
        "POST /v1/ai/item-proposals/{proposalId:guid}/apply",
        "POST /v1/ai/item-proposals/{proposalId:guid}/dismiss",
        "POST /v1/ai/member-proposals/{proposalId:guid}/apply",
        "POST /v1/ai/member-proposals/{proposalId:guid}/dismiss",
        "POST /v1/ai/models/discover",
        "POST /v1/ai/new-item-proposals/{proposalId:guid}/apply",
        "POST /v1/ai/new-item-proposals/{proposalId:guid}/dismiss",
        "POST /v1/ai/openrouter/complete",
        "POST /v1/ai/openrouter/login",
        "POST /v1/ai/project-proposals/{proposalId:guid}/apply",
        "POST /v1/ai/project-proposals/{proposalId:guid}/dismiss",
        "POST /v1/assistant/conversations",
        "POST /v1/assistant/conversations/{conversationId:guid}/messages",
        "POST /v1/cli-login/approve",
        "POST /v1/cli-login/check",
        "POST /v1/client-errors",
        "POST /v1/description-templates",
        "POST /v1/email-settings/test",
        "POST /v1/knowledge/spaces/",
        "POST /v1/knowledge/spaces/{spaceKey}/archive",
        "POST /v1/knowledge/spaces/{spaceKey}/pages",
        "POST /v1/knowledge/spaces/{spaceKey}/pages/{number}/attachments",
        "POST /v1/knowledge/spaces/{spaceKey}/pages/{number}/move",
        "POST /v1/knowledge/spaces/{spaceKey}/pages/{number}/restore",
        "POST /v1/knowledge/spaces/{spaceKey}/trash/purge",
        "POST /v1/knowledge/spaces/{spaceKey}/unarchive",
        "POST /v1/my-work/saved-views",
        "POST /v1/oauth/authorization/approve",
        "POST /v1/oauth/authorization/check",
        "POST /v1/oauth/authorization/deny",
        "POST /v1/people",
        "POST /v1/people/{personId}/disable",
        "POST /v1/people/{personId}/enable",
        "POST /v1/projects",
        "POST /v1/projects/{projectKey}/assistant/attachments",
        "POST /v1/projects/{projectKey}/assistant/conversations",
        "POST /v1/projects/{projectKey}/assistant/conversations/{conversationId:guid}/messages",
        "POST /v1/projects/{projectKey}/saved-views",
        "PUT /v1/account/ai-connections/{connectionId:guid}",
        "PUT /v1/account/email-notifications",
        "PUT /v1/account/password",
        "PUT /v1/ai/connections/{connectionId:guid}",
        "PUT /v1/ai/host/sources/{sourceId:guid}",
        "PUT /v1/ai/settings",
        "PUT /v1/description-templates/{id}",
        "PUT /v1/email-settings",
        "PUT /v1/knowledge/spaces/{spaceKey}",
        "PUT /v1/knowledge/spaces/{spaceKey}/pages/{number}/restriction",
        "PUT /v1/my-work/saved-views/{viewId:guid}",
        "PUT /v1/people/{personId}/password",
        "PUT /v1/people/{personId}/profile",
        "PUT /v1/people/{personId}/role",
        "PUT /v1/projects/{projectKey}/saved-views/{viewId:guid}",
    ];

    [Test]
    public void AnonymousEndpoints_AreExactlyTheKnownList() =>
        AssertList(EndpointKind.Anonymous, Anonymous, "匿名（不用登入）");

    [Test]
    public void MachineCredentialEndpoints_AreExactlyTheKnownList() =>
        AssertList(EndpointKind.MachineCredential, MachineCredential, "主機代理的機器憑證（HostCredentials 中介軟體驗證）");

    [Test]
    public void CookieOnlyEndpoints_AreExactlyTheKnownList() =>
        AssertList(EndpointKind.CookieOnly, CookieOnly, "只能用 Cookie（要求登入，沒有宣告權杖的範圍）");

    [Test]
    public void EveryEndpoint_FallsInExactlyOneCategory()
    {
        // Arrange
        var endpoints = Classify();

        // Act：機器憑證的端點不能同時帶登入的授權或權杖範圍（兩套驗證會同時套上去）；
        // 沒要求登入的端點也不能宣告權杖範圍（範圍永遠不會被檢查）。
        var conflicting = endpoints.Where(entry => entry.IsConflicting).Select(entry => entry.Name).Distinct().Order(StringComparer.Ordinal).ToArray();
        var scoped = endpoints.Where(entry => entry.Kind == EndpointKind.Scoped).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(conflicting, Is.Empty, "同時落在兩類的端點：\n  " + string.Join("\n  ", conflicting));
            Assert.That(scoped, Is.Not.Empty);
            Assert.That(scoped.All(entry => entry.RequiresSignIn && entry.HasScopeRequirement), Is.True);
        }
    }

    // 帶具名授權政策的端點（路由加方法加政策名稱）。政策的內容改了、或別的端點掛上了政策，要有人確認是故意的。
    private static readonly string[] NamedPolicyEndpoints =
    [
        "GET /v1/people => organization-members",
    ];

    [Test]
    public void EndpointsWithANamedAuthorizationPolicy_AreExactlyTheKnownList()
    {
        // Arrange
        var actual = Classify()
            .SelectMany(entry => entry.Policies.Select(policy => $"{entry.Name} => {policy}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.That(actual, Is.EqualTo(NamedPolicyEndpoints.Order(StringComparer.Ordinal).ToArray()), "帶具名授權政策的端點和寫死的清單不一樣。");
    }

    private static void AssertList(EndpointKind kind, string[] expected, string description)
    {
        var actual = Classify().Where(entry => entry.Kind == kind).Select(entry => entry.Name).Distinct().Order(StringComparer.Ordinal).ToArray();
        var expectedSorted = expected.Order(StringComparer.Ordinal).ToArray();
        var added = actual.Except(expectedSorted).ToArray();
        var removed = expectedSorted.Except(actual).ToArray();

        Assert.That(
            actual,
            Is.EqualTo(expectedSorted),
            $"「{description}」的端點清單和寫死的不一樣。\n"
            + $"新增的（不在清單裡）：{(added.Length == 0 ? "（無）" : "\n  " + string.Join("\n  ", added))}\n"
            + $"消失的（清單裡有、實際沒有）：{(removed.Length == 0 ? "（無）" : "\n  " + string.Join("\n  ", removed))}\n"
            + "新加的端點：該給權杖用的，在端點上宣告範圍（RequireTokenScope）；真的只能在網頁上用的，才把它加進只能用 Cookie 的清單；"
            + "匿名的端點要先確認它自己擋得住（限流、驗證內容、不洩漏資料）再加進匿名的清單。\n"
            + $"實際的清單（{actual.Length} 個）：\n  {string.Join("\n  ", actual)}");
    }

    private static List<EndpointEntry> Classify()
    {
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions
            {
                Settings = new Dictionary<string, string?> { [LiveRegistration.EnabledSetting] = "true", [LiveUpdateWorker.EnabledSetting] = "false" },
            });
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
        var entries = new List<EndpointEntry>();
        foreach (var endpoint in endpoints)
        {
            var requiresSignIn = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;
            var hasScope = endpoint.Metadata.Any(item => item.GetType().Name == RequirementTypeName);
            var isMachine = endpoint.Metadata.Any(item => item.GetType().Name == HostAgentTypeName);
            var kind = (isMachine, requiresSignIn, hasScope) switch
            {
                (true, _, _) => EndpointKind.MachineCredential,
                (_, false, _) => EndpointKind.Anonymous,
                (_, true, false) => EndpointKind.CookieOnly,
                (_, true, true) => EndpointKind.Scoped,
            };
            var authorizeData = endpoint.Metadata.OfType<IAuthorizeData>().ToArray();
            var policies = authorizeData.Select(data => data.Policy).OfType<string>().Where(policy => policy.Length > 0).ToArray();
            var isConflicting = (isMachine && (authorizeData.Length > 0 || hasScope)) || (!requiresSignIn && !isMachine && hasScope);
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["(any)"];
            entries.AddRange(methods.Select(method => new EndpointEntry(
                $"{method} {endpoint.RoutePattern.RawText}", kind, requiresSignIn, hasScope, policies, isConflicting)));
        }

        return entries;
    }

    private enum EndpointKind
    {
        Unclassified,
        Anonymous,
        MachineCredential,
        CookieOnly,
        Scoped,
    }

    private sealed record EndpointEntry(
        string Name, EndpointKind Kind, bool RequiresSignIn, bool HasScopeRequirement, string[] Policies, bool IsConflicting);
}
