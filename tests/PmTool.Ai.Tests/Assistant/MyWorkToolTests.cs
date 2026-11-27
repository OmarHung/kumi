using System.Text.Json;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>my_work：專案對話與組織對話都有、只查使用者自己的、日期與逾期照組織時區、參數錯誤回工具的錯誤；舊版 kumi 的工作與提示。</summary>
[TestFixture]
public sealed class MyWorkToolTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _h = await AssistantHarness.StartAsync();
        _h.Projects.OnList = _ => [Project("WEB", "網站改版"), Project("OPS", "維運")];
        _h.WorkItems.OnListAcross = (_, _) => new WorkItemPage([], null);
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [TestCase(false)]
    [TestCase(true)]
    public async Task MyWork_InProjectAndOrganizationConversations_ReturnsTheUsersItemsWithProjectNames(bool organization)
    {
        // Arrange
        _h.WorkItems.OnListAcross = (_, _) => new WorkItemPage(
            [Item("OPS-3", "OPS", new DateOnly(2026, 11, 19)) with { AssigneeId = _h.Actor.PersonId, Priority = Priority.High }], null);
        _h.People.Set(_h.OrganizationId, _h.Actor.PersonId, PmTool.Identity.OrganizationRole.Member, name: "小明");

        // Act
        var result = await RunAsync(new { }, organization);

        // Assert
        var item = result.GetProperty("items")[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("notice").GetString(), Does.Contain("使用者寫的內容"));
            Assert.That(item.GetProperty("key").GetString(), Is.EqualTo("OPS-3"));
            Assert.That(item.GetProperty("projectKey").GetString(), Is.EqualTo("OPS"));
            Assert.That(item.GetProperty("projectName").GetString(), Is.EqualTo("維運"));
            Assert.That(item.GetProperty("dueDate").GetString(), Is.EqualTo("2026-11-19"));
            Assert.That(item.GetProperty("assignee").GetProperty("name").GetString(), Is.EqualTo("小明"));
            Assert.That(item.TryGetProperty("title", out _) && item.TryGetProperty("type", out _) && item.TryGetProperty("state", out _), Is.True);
            Assert.That(item.TryGetProperty("stateCategory", out _) && item.TryGetProperty("priority", out _), Is.True);
            Assert.That(result.GetProperty("hasMore").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task MyWork_ByDefault_AsksForMyOpenAssignedItemsSortedByDueDateAcrossAllProjects()
    {
        // Act
        await RunAsync(new { });

        // Assert
        var call = _h.WorkItems.AcrossCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.Actor, Is.EqualTo(_h.Actor));
            Assert.That(call.Scope, Is.SameAs(CrossProjectScope.All));
            Assert.That(call.Query.AssigneeId, Is.EqualTo(_h.Actor.PersonId));
            Assert.That(call.Query.CreatedBy, Is.Null);
            Assert.That(call.Query.StateCategories, Is.EqualTo(new[] { StateCategory.Backlog, StateCategory.NotStarted, StateCategory.InProgress }));
            Assert.That(call.Query.SortBy, Is.EqualTo(WorkItemSortField.DueDate));
            Assert.That(call.Query.Limit, Is.EqualTo(MyWorkTestLimits.DefaultPlusOne));
        }
    }

    [Test]
    public async Task MyWork_Created_FiltersByCreatorInsteadOfAssignee()
    {
        // Arrange
        var other = Guid.NewGuid();
        _h.WorkItems.OnListAcross = (_, _) => new WorkItemPage([Item("WEB-1", "WEB", null) with { AssigneeId = other }], null);
        _h.People.Set(_h.OrganizationId, other, PmTool.Identity.OrganizationRole.Member, name: "小華");

        // Act
        var result = await RunAsync(new { scope = "created" });

        // Assert
        var query = _h.WorkItems.AcrossCalls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.CreatedBy, Is.EqualTo(_h.Actor.PersonId));
            Assert.That(query.AssigneeId, Is.Null);
            Assert.That(result.GetProperty("items")[0].GetProperty("assignee").GetProperty("name").GetString(), Is.EqualTo("小華"));
        }
    }

    [Test]
    public async Task MyWork_DueRange_IsPassedToTheQuery()
    {
        // Act
        await RunAsync(new { dueFrom = "2026-11-19", dueTo = "2026-11-20" });

        // Assert
        var query = _h.WorkItems.AcrossCalls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.DueFrom, Is.EqualTo(new DateOnly(2026, 11, 19)));
            Assert.That(query.DueTo, Is.EqualTo(new DateOnly(2026, 11, 20)));
        }
    }

    [Test]
    public async Task MyWork_Overdue_MeansDueBeforeTodayInTheOrganizationTimeZone()
    {
        // Arrange：UTC 11-18 08:00 是台北 16:00；再過 9 小時，UTC 還是 11-18、台北已經是 11-19。
        _h.Clock.Advance(TimeSpan.FromHours(9));

        // Act
        await RunAsync(new { overdue = true });

        // Assert
        var query = _h.WorkItems.AcrossCalls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.DueTo, Is.EqualTo(new DateOnly(2026, 11, 18)));
            Assert.That(query.StateCategories, Is.EqualTo(new[] { StateCategory.Backlog, StateCategory.NotStarted, StateCategory.InProgress }));
        }
    }

    [Test]
    public async Task MyWork_OverdueWithAnEarlierDueTo_KeepsTheEarlierDay()
    {
        // Act
        await RunAsync(new { overdue = true, dueTo = "2026-11-10" });

        // Assert
        Assert.That(_h.WorkItems.AcrossCalls.Single().Query.DueTo, Is.EqualTo(new DateOnly(2026, 11, 10)));
    }

    [Test]
    public async Task MyWork_Done_AsksForTheDoneCategoryOnly()
    {
        // Act
        await RunAsync(new { stateCategory = "done", dueFrom = "2026-11-16", dueTo = "2026-11-22" });

        // Assert
        Assert.That(_h.WorkItems.AcrossCalls.Single().Query.StateCategories, Is.EqualTo(new[] { StateCategory.Done }));
    }

    [Test]
    public async Task MyWork_InProgressCategory_AsksForThatCategoryOnly()
    {
        // Act
        await RunAsync(new { stateCategory = "in_progress" });

        // Assert
        Assert.That(_h.WorkItems.AcrossCalls.Single().Query.StateCategories, Is.EqualTo(new[] { StateCategory.InProgress }));
    }

    [TestCase(999, 51)]
    [TestCase(0, 2)]
    [TestCase(5, 6)]
    public async Task MyWork_Limit_IsClampedToOneToFifty(int limit, int expectedQueryLimit)
    {
        // Act
        await RunAsync(new { limit });

        // Assert：多要一筆，才知道還有沒有。
        Assert.That(_h.WorkItems.AcrossCalls.Single().Query.Limit, Is.EqualTo(expectedQueryLimit));
    }

    [Test]
    public async Task MyWork_MoreThanTheLimit_ReturnsOnlyTheLimitAndSaysThereIsMore()
    {
        // Arrange
        _h.WorkItems.OnListAcross = (_, _) => new WorkItemPage(
            [Item("WEB-1", "WEB", null), Item("WEB-2", "WEB", null), Item("WEB-3", "WEB", null)], null);

        // Act
        var result = await RunAsync(new { limit = 2 });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("items").GetArrayLength(), Is.EqualTo(2));
            Assert.That(result.GetProperty("hasMore").GetBoolean(), Is.True);
        }
    }

    [TestCase("""{"scope":"everyone"}""")]
    [TestCase("""{"stateCategory":"canceled"}""")]
    [TestCase("""{"stateCategory":"所有"}""")]
    [TestCase("""{"dueFrom":"明天"}""")]
    [TestCase("""{"dueTo":"2026/11/20"}""")]
    [TestCase("""{"dueTo":"2026-02-30"}""")]
    [TestCase("""{"overdue":true,"stateCategory":"done"}""")]
    [TestCase("{\"scope\":\"assigned\\n\"}")]
    public async Task MyWork_BadArguments_AreAToolErrorAndNeverReachTheWorkEngine(string json)
    {
        // Act
        var result = await RunAsync(JsonDocument.Parse(json).RootElement);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Is.Not.Empty);
            Assert.That(_h.WorkItems.AcrossCalls, Is.Empty);
        }
    }

    [Test]
    public async Task MyWork_WorkEngineFailure_IsReportedAsAToolError()
    {
        // Arrange
        _h.WorkItems.OnListAcross = (_, _) => WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.QueryInvalid, "條件不對。");

        // Act
        var result = await RunAsync(new { });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Is.EqualTo("條件不對。"));
        }
    }

    [Test]
    public void ToolLists_FollowTheAgentVersion()
    {
        // Act
        var oldProject = AgentToolLists.For("WEB", "0.17.0");
        var newProject = AgentToolLists.For("WEB", AiRules.MinAgentVersionForMyWork);
        var oldOrganization = AgentToolLists.For(null, "0.17.0");
        var newOrganization = AgentToolLists.For(null, AiRules.MinAgentVersionForMyWork);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldProject, Does.Not.Contain("my_work"));
            Assert.That(newProject, Does.Contain("my_work"));
            Assert.That(oldOrganization, Does.Not.Contain("my_work"));
            Assert.That(newOrganization, Does.Contain("my_work"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AgentPrompts_ForAKumiThatIsTooOld_DropTheToolLineAndSayToUpdate(bool host)
    {
        // Act
        var project = host
            ? LocalPromptBuilder.HostSystemPrompt("WEB", [], supportsMyWork: false)
            : LocalPromptBuilder.SystemPrompt("WEB", [], supportsMyWork: false);
        var organization = host
            ? LocalPromptBuilder.HostOrganizationSystemPrompt(true, supportsMyWork: false)
            : LocalPromptBuilder.OrganizationSystemPrompt(true, supportsMyWork: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var prompt in new[] { project, organization })
            {
                Assert.That(prompt, Does.Not.Contain("- my_work："));
                Assert.That(prompt, Does.Contain("這一次沒有 my_work 這個工具").And.Contain("kumi"));
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AgentPrompts_ForACurrentKumi_ListTheToolWithoutAnUpdateNote(bool host)
    {
        // Act
        var project = host ? LocalPromptBuilder.HostSystemPrompt("WEB", []) : LocalPromptBuilder.SystemPrompt("WEB", []);
        var organization = host ? LocalPromptBuilder.HostOrganizationSystemPrompt(true) : LocalPromptBuilder.OrganizationSystemPrompt(true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var prompt in new[] { project, organization })
            {
                Assert.That(prompt, Does.Contain("- my_work："));
                Assert.That(prompt, Does.Not.Contain("這一次沒有 my_work"));
            }
        }
    }

    [Test]
    public void SystemPrompts_TellTheModelToUseMyWorkForTheUsersOwnWork()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AssistantPrompt.SystemPrompt, Does.Contain("my_work"));
            Assert.That(AssistantPrompt.OrganizationSystemPrompt, Does.Contain("my_work"));
            Assert.That(AssistantPrompt.OrganizationSystemPrompt, Does.Not.Contain("你讀不到任何專案的設定或項目"));
        }
    }

    private static ProjectSummary Project(string key, string name) =>
        new(Guid.NewGuid(), key, name, 1, "member", DateTimeOffset.UnixEpoch);

    private static WorkItem Item(string key, string project, DateOnly? due) =>
        ItemSamples.Item(key, "寫報告", project) with { DueDate = due };

    private async Task<JsonElement> RunAsync(object arguments, bool organization = false)
    {
        var conversation = organization ? await _h.NewOrganizationConversationAsync() : await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("call-1", "my_work", arguments)).Reply("好");
        if (organization)
        {
            await _h.SendOrganizationAsync(conversation.Id, "我明天有哪些工作");
        }
        else
        {
            await _h.SendAsync(conversation.Id);
        }

        var result = _h.Script.Calls.Last().Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
        using var document = JsonDocument.Parse((string)result.Result!);
        return document.RootElement.Clone();
    }
}

internal static class MyWorkTestLimits
{
    /// <summary>預設 30 筆，工具多要一筆來判斷還有沒有。</summary>
    public const int DefaultPlusOne = 31;
}
