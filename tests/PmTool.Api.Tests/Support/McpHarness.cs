using PmTool.Abstractions;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

/// <summary>
/// 一個連好的 MCP 用戶端加它背後的假模組：測試用 <see cref="Factory"/> 的 Modules 設定模組的回應、檢查模組收到的呼叫，
/// 用 <see cref="CallAsync"/> 呼叫工具。
/// </summary>
internal sealed class McpHarness : IAsyncDisposable
{
    private McpHarness(ApiFactory factory, FakeBlueprintUsageService usage, McpClient client, AuthenticatedUser user)
    {
        Factory = factory;
        Usage = usage;
        Client = client;
        User = user;
    }

    public ApiFactory Factory { get; }

    public FakeModules Modules => Factory.Modules;

    public FakeBlueprintUsageService Usage { get; }

    public McpClient Client { get; }

    public AuthenticatedUser User { get; }

    /// <summary>用授權發出的權杖（有授權編號）連線；沒指定範圍就是四個全有。</summary>
    public static Task<McpHarness> StartAsync(params AccessTokenScope[] scopes) => StartAsync(TestUsers.Member, scopes);

    public static Task<McpHarness> StartAsync(AuthenticatedUser user, params AccessTokenScope[] scopes) =>
        StartAsync(user, project: null, scopes);

    /// <summary>用個人存取權杖（本機代理的那種）連線，帶 <c>X-Kumi-Project</c> 標頭（<paramref name="project"/> 是 null 就不帶）。</summary>
    public static Task<McpHarness> StartForAgentAsync(string? project, params AccessTokenScope[] scopes) =>
        StartAsync(TestUsers.Member, project, scopes);

    /// <summary>本機代理的連線，但不帶 <c>X-Kumi-Job</c>（舊版 kumi）。</summary>
    public static Task<McpHarness> StartForAgentWithoutJobAsync(string project, params AccessTokenScope[] scopes) =>
        StartAsync(TestUsers.Member, project, scopes, sendJob: false);

    public static Task<McpHarness> StartAsync(AuthenticatedUser user, string? project, params AccessTokenScope[] scopes) =>
        StartAsync(user, project, scopes, sendJob: true);

    /// <summary>
    /// 不選專案的組織對話的本機代理連線：只帶 <c>X-Kumi-Job</c>、不帶專案標頭，假的額度把這件工作當成沒有專案的對話。
    /// </summary>
    public static Task<McpHarness> StartForOrganizationJobAsync(params AccessTokenScope[] scopes) =>
        StartAsync(TestUsers.Member, project: null, scopes, sendJob: true, organizationJob: true);

    private static async Task<McpHarness> StartAsync(
        AuthenticatedUser user, string? project, AccessTokenScope[] scopes, bool sendJob, bool organizationJob = false)
    {
        var usage = new FakeBlueprintUsageService();
        var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions
            {
                // 設定使用狀況的服務與模板庫只有 MCP 與 AI 助理用；Program.cs 接上的是真的（模板庫要用真的驗證器載入），測試換成假的。
                ConfigureServices = services =>
                {
                    services.RemoveAll<IBlueprintUsageService>();
                    services.AddSingleton<IBlueprintUsageService>(usage);
                    services.RemoveAll<ITemplateLibrary>();
                    services.AddSingleton<ITemplateLibrary>(new FakeTemplateLibrary());
                    // 組織的時區存在身分模組的資料表裡；這裡的資料庫是假的，換成固定的預設時區。
                    services.RemoveAll<IOrganizationClock>();
                    services.AddSingleton<IOrganizationClock>(new FixedOrganizationClock());
                },
            });

        // 負責人只在專案的成員裡找：專案有 Owner 與 Member 兩位成員，Guest 在組織裡卻不是成員。
        factory.Modules.Projects.OnGet = (_, key) => new ProjectDetail(
            TestData.Project(key),
            TestData.Blueprint(),
            [new ProjectMember(TestUsers.Owner.PersonId, "editor"), new ProjectMember(TestUsers.Member.PersonId, "editor")],
            [WorkPermissions.ItemRead, WorkPermissions.ProjectManage]);
        if (organizationJob)
        {
            factory.Modules.AgentBudget.OnFindJobScope = (_, _) => FakeAgentToolBudget.OrganizationJob;
        }
        else if (project is not null)
        {
            // 伺服器用工作找到對話的專案：假的額度回連線帶的那個專案（和標頭相同，既有的測試照樣過）。
            var jobScope = FakeAgentToolBudget.ProjectJob(project.ToUpperInvariant());
            factory.Modules.AgentBudget.OnFindJobScope = (_, _) => jobScope;
        }

        var secret = McpTesting.GrantToken(factory, user, scopes.Length == 0 ? McpTesting.AllScopes : scopes);
        return new McpHarness(
            factory, usage, await McpTesting.ConnectAsync(factory, secret, project, sendJob: sendJob, organizationJob: organizationJob), user);
    }

    public Task<(bool IsError, JsonNode Body)> CallAsync(string tool, object? arguments = null) =>
        McpTesting.CallAsync(Client, tool, arguments);

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        Factory.Dispose();
    }
}

/// <summary>永遠回預設時區的組織時鐘，不碰資料庫。</summary>
internal sealed class FixedOrganizationClock : IOrganizationClock
{
    public Task<TimeZoneInfo> TimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(TimeZoneInfo.FindSystemTimeZoneById(OrganizationTime.DefaultTimeZoneId));
}
