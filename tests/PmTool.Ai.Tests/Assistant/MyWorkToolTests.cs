using System.Text.Json;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>my_work：專案對話與組織對話都有、只查使用者自己的、日期與逾期照組織時區、參數錯誤回工具的錯誤；舊版 kumi 的工作與提示。</summary>
[TestFixture]
public sealed class MyWorkToolTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [TestCase(false)]
    [TestCase(true)]
    public async Task MyWork_InProjectAndOrganizationConversations_ReturnsTheFoundItemsInTheSameShape(bool organization)
    {
        // Arrange
        var me = new FoundPerson(_h.Actor.PersonId, "小明");
        _h.ItemFinder.OnFind = _ => new ItemFindResult(
            [Found("OPS-3", new DateOnly(2026, 11, 19)) with { Assignee = me, Priority = "high" }], false);

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
            Assert.That(item.GetProperty("priority").GetString(), Is.EqualTo("high"));
            Assert.That(item.GetProperty("assignee").GetProperty("name").GetString(), Is.EqualTo("小明"));
            Assert.That(item.TryGetProperty("title", out _) && item.TryGetProperty("type", out _) && item.TryGetProperty("state", out _), Is.True);
            Assert.That(item.TryGetProperty("stateCategory", out _), Is.True);
            Assert.That(item.TryGetProperty("createdBy", out _), Is.False);
            Assert.That(result.GetProperty("hasMore").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task MyWork_ByDefault_AsksTheFinderForMyItemsWithoutAConversation()
    {
        // Act
        await RunAsync(new { });

        // Assert：只會是自己的項目，所以不帶對話編號（不記讀過的專案）。
        var call = _h.ItemFinder.Calls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.Actor, Is.EqualTo(_h.Actor));
            Assert.That(call.ConversationId, Is.Null);
            Assert.That(call.Query.Assignee, Is.EqualTo(ItemFindAssignee.Me));
            Assert.That(call.Query.CreatedBy, Is.Null);
            Assert.That(call.Query.StateCategories, Is.Empty);
            Assert.That(call.Query.Overdue, Is.False);
            Assert.That(call.Query.Limit, Is.EqualTo(MyWorkTestLimits.Default));
        }
    }

    [Test]
    public async Task MyWork_Created_FiltersByCreatorInsteadOfAssignee()
    {
        // Act
        await RunAsync(new { scope = "created" });

        // Assert
        var query = _h.ItemFinder.Calls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.CreatedBy, Is.EqualTo(_h.Actor.PersonId));
            Assert.That(query.Assignee, Is.EqualTo(ItemFindAssignee.Anyone));
        }
    }

    [Test]
    public async Task MyWork_DueRange_IsPassedToTheQuery()
    {
        // Act
        await RunAsync(new { dueFrom = "2026-11-19", dueTo = "2026-11-20" });

        // Assert
        var query = _h.ItemFinder.Calls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.DueFrom, Is.EqualTo(new DateOnly(2026, 11, 19)));
            Assert.That(query.DueTo, Is.EqualTo(new DateOnly(2026, 11, 20)));
        }
    }

    [Test]
    public async Task MyWork_Overdue_LeavesTheTodayMathAndTheOpenCategoriesToTheFinder()
    {
        // Act
        await RunAsync(new { overdue = true, stateCategory = "in_progress", dueTo = "2026-11-10" });

        // Assert
        var query = _h.ItemFinder.Calls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.Overdue, Is.True);
            Assert.That(query.DueTo, Is.EqualTo(new DateOnly(2026, 11, 10)));
            Assert.That(query.StateCategories, Is.Empty);
        }
    }

    [TestCase("done", "done")]
    [TestCase("in_progress", "in_progress")]
    [TestCase("not_started", "not_started")]
    public async Task MyWork_StateCategory_AsksForThatCategoryOnly(string category, string expected)
    {
        // Act
        await RunAsync(new { stateCategory = category });

        // Assert
        Assert.That(_h.ItemFinder.Calls.Single().Query.StateCategories, Is.EqualTo(new[] { expected }));
    }

    [TestCase(999, 50)]
    [TestCase(0, 1)]
    [TestCase(5, 5)]
    public async Task MyWork_Limit_IsClampedToOneToFifty(int limit, int expected)
    {
        // Act
        await RunAsync(new { limit });

        // Assert
        Assert.That(_h.ItemFinder.Calls.Single().Query.Limit, Is.EqualTo(expected));
    }

    [Test]
    public async Task MyWork_HasMoreFromTheFinder_IsPassedOn()
    {
        // Arrange
        _h.ItemFinder.OnFind = _ => new ItemFindResult([Found("WEB-1", null)], true);

        // Act
        var result = await RunAsync(new { limit = 1 });

        // Assert
        Assert.That(result.GetProperty("hasMore").GetBoolean(), Is.True);
    }

    [TestCase("""{"scope":"everyone"}""")]
    [TestCase("""{"stateCategory":"canceled"}""")]
    [TestCase("""{"stateCategory":"所有"}""")]
    [TestCase("""{"dueFrom":"明天"}""")]
    [TestCase("""{"dueTo":"2026/11/20"}""")]
    [TestCase("""{"dueTo":"2026-02-30"}""")]
    [TestCase("""{"overdue":true,"stateCategory":"done"}""")]
    [TestCase("{\"scope\":\"assigned\\n\"}")]
    public async Task MyWork_BadArguments_AreAToolErrorAndNeverReachTheFinder(string json)
    {
        // Act
        var result = await RunAsync(JsonDocument.Parse(json).RootElement);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Is.Not.Empty);
            Assert.That(_h.ItemFinder.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task MyWork_FinderFailure_IsReportedAsAToolError()
    {
        // Arrange
        _h.ItemFinder.OnFind = _ => WorkSamples.Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "條件不對。");

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

    private static FoundItem Found(string key, DateOnly? due) =>
        new(key, key.Split('-')[0], "維運", "寫報告", "task", "todo", "not_started", due, "none", null, new FoundPerson(Guid.NewGuid(), "建立者"));

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
    /// <summary>沒給 limit 時的筆數。</summary>
    public const int Default = 30;
}
