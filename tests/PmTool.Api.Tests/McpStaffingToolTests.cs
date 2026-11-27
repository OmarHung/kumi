using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Mcp;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>MCP 的派工工具：get_project、list_people（items:read）與 propose_member_changes（agent:run，只建立建議）。</summary>
[TestFixture]
public sealed class McpStaffingToolTests
{
    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite];

    private static readonly Guid OtherOrganizationId = Guid.Parse("0190a000-0000-7000-8000-000000000999");

    [Test]
    public async Task GetProject_ReturnsTheBriefFromTheSharedServiceInTheDocumentedShape()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("get_project", new { projectKey = "WEB" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(mcp.Modules.Staffing.ProjectCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), "WEB")));
            Assert.That(body["key"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(body["myPermissions"]?.AsArray().Select(p => p!.GetValue<string>()), Does.Contain("project.manage"));
            Assert.That(body["itemTypes"]?[0]?["firstState"]?.GetValue<string>(), Is.EqualTo("待辦"));
            Assert.That(body["itemTypes"]?[0]?["fields"]?[0]?["options"]?[0]?.GetValue<string>(), Is.EqualTo("infra"));
            var member = body["members"]![0]!;
            Assert.That(member["id"]?.GetValue<Guid>(), Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(member["jobTitle"]?.GetValue<string>(), Is.EqualTo("前端工程師"));
            Assert.That(member["responsibilities"]?.GetValue<string>(), Is.EqualTo("WordPress 維運"));
            Assert.That(member["openItems"]?.GetValue<int>(), Is.EqualTo(3));
            Assert.That(body["roles"]?[0]?["permissions"]?.AsArray(), Has.Count.EqualTo(2));
            Assert.That(body["memberManagers"]?[0]?.GetValue<string>(), Is.EqualTo("甲"));
            Assert.That(body["cycles"]?[0]?["id"]?.GetValue<Guid>(), Is.EqualTo(FakeStaffingBrief.CycleId));
            Assert.That(body["cycles"]?[0]?["startDate"]?.GetValue<string>(), Is.EqualTo("2026-11-30"));
            Assert.That(body["milestones"]?[0]?["dueDate"]?.GetValue<string>(), Is.EqualTo("2026-12-31"));
            Assert.That(body["notice"]?.GetValue<string>(), Does.Contain("使用者寫的內容"));
        }
    }

    [Test]
    public async Task GetProject_OtherProjectThanTheConversations_IsRefusedWithoutCallingTheService()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("get_project", new { projectKey = "OPS" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.Staffing.ProjectCalls, Is.Empty);
        }
    }

    [Test]
    public async Task GetProject_ServiceFailure_IsAToolError()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.Staffing.OnGetProject = _ => new Failure(FailureKind.NotFound, "project.not_found", "找不到專案。");

        // Act
        var (isError, body) = await mcp.CallAsync("get_project", new { projectKey = "WEB" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["code"]?.GetValue<string>(), Is.EqualTo("project.not_found"));
        }
    }

    [Test]
    public async Task ListPeople_WithoutProjectKey_UsesTheConversationsProjectAndPassesTheFilters()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("list_people", new { text = "WordPress", limit = 5 });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            var call = mcp.Modules.Staffing.PeopleCalls.Single();
            Assert.That(call.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(call.Query, Is.EqualTo(new PeopleQuery("WEB", "WordPress", 5)));
            var person = body["people"]![0]!;
            Assert.That(person["id"]?.GetValue<Guid>(), Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(person["organizationRole"]?.GetValue<string>(), Is.EqualTo("member"));
            Assert.That(person["projectRole"]?.GetValue<string>(), Is.EqualTo("editor"));
            Assert.That(person.AsObject().ContainsKey("email"), Is.False, "不給 Email");
            Assert.That(body["notice"]?.GetValue<string>(), Does.Contain("使用者寫的內容"));
        }
    }

    [Test]
    public async Task ListPeople_ForAGeneralToken_NeedsNoProjectAndDefaultsToTheMaximumLimit()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (isError, _) = await mcp.CallAsync("list_people");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.Staffing.PeopleCalls.Single().Query, Is.EqualTo(new PeopleQuery(null, null, AiRules.MaxPeopleListed)));
        }
    }

    [TestCase("""{"projectKey":"OPS"}""")]
    [TestCase("""{"limit":0}""")]
    [TestCase("""{"limit":101}""")]
    [TestCase("""{"limit":"many"}""")]
    [TestCase("""{"extra":1}""")]
    public async Task ListPeople_BadArgumentsOrAnotherProject_AreToolErrorsWithoutCallingTheService(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("list_people", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.Staffing.PeopleCalls, Is.Empty);
        }
    }

    [Test]
    public async Task ListPeople_Guest_GetsTheServicesRefusalAsAToolError()
    {
        // Arrange：訪客不能看組織的人員名單（規則在模組，工具原樣回它的失敗）。
        await using var mcp = await McpHarness.StartAsync(TestUsers.Guest, "WEB", AgentScopes);
        mcp.Modules.Staffing.OnListPeople = _ => new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "訪客看不到組織的人員名單。");

        // Act
        var (isError, body) = await mcp.CallAsync("list_people");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Is.EqualTo("訪客看不到組織的人員名單。"));
        }
    }

    [Test]
    public async Task ProposeMemberChanges_ResolvesPeopleAcrossTheOrganizationAndPassesRolesAndRemovals()
    {
        // Arrange：Guest 不在假專案的成員裡，但在組織裡：加入的人本來就還不是成員。
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_member_changes", new
        {
            projectKey = "WEB",
            changes = new object[]
            {
                new { person = TestUsers.Guest.Email.ToUpperInvariant(), role = "contributor" },
                new { person = TestUsers.Owner.PersonId.ToString(), role = "editor" },
                new { person = "me", remove = true },
            },
        });

        // Assert
        var create = mcp.Modules.MemberProposals.Creates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(create.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(create.Draft.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(create.Draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId), "對話編號來自伺服器找到的工作，撤回才找得到剛提出的建議");
            Assert.That(
                create.Draft.Changes,
                Is.EqualTo(new[]
                {
                    new MemberChangeDraft(TestUsers.Guest.PersonId, "contributor"),
                    new MemberChangeDraft(TestUsers.Owner.PersonId, "editor"),
                    new MemberChangeDraft(TestUsers.Member.PersonId, null),
                }));
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeMemberProposals.ProposalId));
            Assert.That(body["changes"]?.AsArray(), Has.Count.EqualTo(3), "結果回的是模組建立出來的建議");
            Assert.That(body["changes"]?[1]?["previousRoleName"]?.GetValue<string>(), Is.EqualTo("執行者"));
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("套用").And.Contain("不要說已經加好了"));
        }
    }

    [TestCase("""{"projectKey":"WEB","changes":[{"person":"me"}]}""", Description = "沒有 role 也沒有 remove")]
    [TestCase("""{"projectKey":"WEB","changes":[{"person":"me","role":"editor","remove":true}]}""", Description = "role 和 remove 同時給")]
    [TestCase("""{"projectKey":"WEB","changes":[{"role":"editor"}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"person":"me","role":"editor","extra":1}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"person":"me","remove":"yes"}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"person":"none","role":"editor"}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[5]}""")]
    [TestCase("""{"projectKey":"WEB","changes":"not json"}""")]
    [TestCase("""{"projectKey":"WEB"}""")]
    [TestCase("""{"changes":[{"person":"me","role":"editor"}]}""")]
    [TestCase("""{"projectKey":"WEB","changes":[{"person":"me","role":"editor"}],"extra":1}""")]
    [TestCase("""{"projectKey":"OPS","changes":[{"person":"me","role":"editor"}]}""", Description = "不是對話的專案")]
    public async Task ProposeMemberChanges_BadArguments_AreToolErrorsAndNothingIsCreated(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_member_changes", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.MemberProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeMemberChanges_MoreChangesThanTheLimit_IsRefusedBeforeLookingAnyoneUp()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act：每一筆的人都找不到；先逐筆查的話錯誤會是找不到人。
        var (isError, body) = await mcp.CallAsync("propose_member_changes", new
        {
            projectKey = "WEB",
            changes = Enumerable.Range(0, AiRules.MaxMemberChangesPerProposal + 1).Select(_ => new { person = "nobody@example.com", role = "editor" }).ToArray(),
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain(AiRules.MaxMemberChangesPerProposal.ToString()).And.Not.Contain("找不到這個人"));
        }
    }

    [Test]
    public async Task ProposeMemberChanges_PersonUnknownDisabledOrInAnotherOrganization_AllGetTheSameError()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        var disabled = new PersonSummary(Guid.NewGuid(), "停用的人", "gone@example.com", OrganizationRole.Member, IsDisabled: true);
        var foreign = new PersonSummary(Guid.NewGuid(), "別的組織", "foreign@example.com", OrganizationRole.Member, IsDisabled: false);
        mcp.Modules.People.Add(TestUsers.OrganizationId, disabled);
        mcp.Modules.People.Add(OtherOrganizationId, foreign);

        // Act
        var errors = new List<string?>();
        foreach (var person in new[] { "nobody@example.com", disabled.Email!, disabled.PersonId.ToString(), foreign.Email!, foreign.PersonId.ToString(), Guid.NewGuid().ToString() })
        {
            var (isError, body) = await mcp.CallAsync(
                "propose_member_changes", new { projectKey = "WEB", changes = new[] { new { person, role = "editor" } } });
            Assert.That(isError, Is.True, person);
            errors.Add(body["error"]?.GetValue<string>()?.Replace(person, "X"));
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(errors.Distinct().Single(), Does.StartWith("changes[0]：找不到這個人"));
            Assert.That(mcp.Modules.MemberProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeMemberChanges_GuestCaller_CannotProbeTheOrganizationByEmail()
    {
        // Arrange：訪客只能在專案成員裡找（AG-10）；Owner 是假專案的成員，Guest 自己不是。
        await using var mcp = await McpHarness.StartAsync(TestUsers.Guest, "WEB", AgentScopes);
        mcp.Modules.People.Add(
            TestUsers.OrganizationId, new PersonSummary(Guid.NewGuid(), "路人", "stranger@example.com", OrganizationRole.Member, IsDisabled: false));

        // Act
        var (strangerIsError, _) = await mcp.CallAsync(
            "propose_member_changes", new { projectKey = "WEB", changes = new[] { new { person = "stranger@example.com", role = "editor" } } });
        var (memberIsError, _) = await mcp.CallAsync(
            "propose_member_changes", new { projectKey = "WEB", changes = new[] { new { person = TestUsers.Owner.Email, role = "viewer" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(strangerIsError, Is.True);
            Assert.That(memberIsError, Is.False, "專案成員照舊找得到");
            Assert.That(mcp.Modules.MemberProposals.Creates.Single().Draft.Changes.Single().PersonId, Is.EqualTo(TestUsers.Owner.PersonId));
        }
    }

    [Test]
    public async Task ProposeMemberChanges_CreatorFailure_IsAToolErrorWithTheMessageAndIssues()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.MemberProposals.OnCreate = _ => new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "你沒有管理這個專案成員的權限。能管理成員的是：甲，以及組織管理員。");

        // Act
        var (isError, body) = await mcp.CallAsync(
            "propose_member_changes", new { projectKey = "WEB", changes = new[] { new { person = "me", role = "editor" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("能管理成員的是：甲"));
            Assert.That(body["code"]?.GetValue<string>(), Is.EqualTo(AiErrorCodes.Forbidden));
        }
    }

    [Test]
    public async Task GeneralToken_SeesTheReadToolsButNotProposeMemberChanges()
    {
        // Arrange：一般的 MCP 用戶端沒有 agent:run；它不能提出成員建議。
        await using var mcp = await McpHarness.StartAsync([.. AccessTokenScopes.Grantable]);

        // Act
        var tools = await mcp.Client.ListToolsAsync();
        var (isError, body) = await mcp.CallAsync(
            "propose_member_changes", new { projectKey = "WEB", changes = new[] { new { person = "me", role = "editor" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(t => t.Name), Does.Contain("get_project").And.Contain("list_people").And.Not.Contain("propose_member_changes"));
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("agent:run"));
            Assert.That(mcp.Modules.MemberProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task AgentToken_SeesAllThreeToolsAndTheyAreInTheAgentAllowList()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(tools.Select(t => t.Name), Does.Contain("get_project").And.Contain("list_people").And.Contain("propose_member_changes"));
    }

    [Test]
    public async Task OrganizationOnlyJob_CanOnlyUseListPeopleOfTheStaffingTools()
    {
        // Arrange：不選專案的組織對話只有 list_people 一個派工的工具（沒有專案可看、可提成員建議）。
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(tools.Select(t => t.Name), Does.Not.Contain("get_project").And.Contain("list_people").And.Not.Contain("propose_member_changes"));
    }

    [Test]
    public void HostProposalTools_CountsProposeMemberChangesAsAProposal() =>
        Assert.That(HostProposalTools.IsProposal("propose_member_changes"), Is.True);
}
