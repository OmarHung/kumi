using System.Text.Json;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>find_items：兩種對話都有、組織對話另有 list_people；參數轉成查詢、對話編號帶進服務；舊版 kumi 的工作與提示。</summary>
[TestFixture]
public sealed class FindItemsToolTests
{
    private static readonly Guid Afang = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Reply_InAProjectConversation_OffersFindItems()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        await _h.SendAsync(conversation.Id);

        // Assert
        Assert.That(_h.Script.Calls.Single().Options!.Tools!.Select(t => t.Name), Does.Contain("find_items").And.Contain("list_people"));
    }

    [Test]
    public async Task Reply_InAnOrganizationConversation_OffersFindItemsAndListPeople()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id);

        // Assert
        Assert.That(_h.Script.Calls.Single().Options!.Tools!.Select(t => t.Name), Does.Contain("find_items").And.Contain("list_people"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FindItems_PassesTheConversationAndTheUsersActorToTheFinder(bool organization)
    {
        // Act
        var (conversationId, _) = await RunAsync(new { }, organization);

        // Assert
        var call = _h.ItemFinder.Calls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.Actor, Is.EqualTo(_h.Actor));
            Assert.That(call.ConversationId, Is.EqualTo(conversationId));
        }
    }

    [Test]
    public async Task FindItems_WithoutArguments_AsksForAnyoneAcrossAllProjectsWithTheDefaultLimit()
    {
        // Act
        await RunAsync(new { });

        // Assert
        var query = _h.ItemFinder.Calls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.Assignee, Is.EqualTo(ItemFindAssignee.Anyone));
            Assert.That(query.AssigneeId, Is.Null);
            Assert.That(query.CreatedBy, Is.Null);
            Assert.That(query.ProjectKey, Is.Null);
            Assert.That(query.StateCategories, Is.Empty);
            Assert.That(query.Overdue, Is.False);
            Assert.That(query.Text, Is.Null);
            Assert.That(query.Limit, Is.EqualTo(30));
        }
    }

    [TestCase("anyone", ItemFindAssignee.Anyone)]
    [TestCase("me", ItemFindAssignee.Me)]
    [TestCase("none", ItemFindAssignee.Nobody)]
    public async Task FindItems_AssigneeKeywords_AreConverted(string assignee, ItemFindAssignee expected)
    {
        // Act
        await RunAsync(new { assignee });

        // Assert
        var query = _h.ItemFinder.Calls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.Assignee, Is.EqualTo(expected));
            Assert.That(query.AssigneeId, Is.Null);
        }
    }

    [Test]
    public async Task FindItems_AssigneeAPersonId_IsPassedAsThatPerson()
    {
        // Act
        await RunAsync(new { assignee = Afang.ToString() });

        // Assert
        var query = _h.ItemFinder.Calls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.Assignee, Is.EqualTo(ItemFindAssignee.Person));
            Assert.That(query.AssigneeId, Is.EqualTo(Afang));
        }
    }

    [Test]
    public async Task FindItems_CreatedByMe_BecomesTheAskersId()
    {
        // Act
        await RunAsync(new { createdBy = "me" });

        // Assert
        Assert.That(_h.ItemFinder.Calls.Single().Query.CreatedBy, Is.EqualTo(_h.Actor.PersonId));
    }

    [Test]
    public async Task FindItems_CreatedByAPersonId_IsPassedThrough()
    {
        // Act
        await RunAsync(new { createdBy = Afang.ToString() });

        // Assert
        Assert.That(_h.ItemFinder.Calls.Single().Query.CreatedBy, Is.EqualTo(Afang));
    }

    [Test]
    public async Task FindItems_DatesProjectCategoriesOverdueAndText_AreConverted()
    {
        // Act
        await RunAsync(new
        {
            projectKey = "WEB",
            stateCategory = new[] { "in_progress", "not_started", "in_progress" },
            dueFrom = "2026-11-30",
            dueTo = "2026-12-06",
            overdue = true,
            text = " 首頁 ",
            limit = 10,
        });

        // Assert
        var query = _h.ItemFinder.Calls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(query.StateCategories, Is.EqualTo(new[] { "in_progress", "not_started" }));
            Assert.That(query.DueFrom, Is.EqualTo(new DateOnly(2026, 11, 30)));
            Assert.That(query.DueTo, Is.EqualTo(new DateOnly(2026, 12, 6)));
            Assert.That(query.Overdue, Is.True);
            Assert.That(query.Text, Is.EqualTo("首頁"));
            Assert.That(query.Limit, Is.EqualTo(10));
        }
    }

    [Test]
    public async Task FindItems_AStateCategoryGivenAsOneString_IsAccepted()
    {
        // Act
        await RunAsync(new { stateCategory = "done" });

        // Assert
        Assert.That(_h.ItemFinder.Calls.Single().Query.StateCategories, Is.EqualTo(new[] { "done" }));
    }

    [TestCase(999, 50)]
    [TestCase(0, 1)]
    public async Task FindItems_Limit_IsClampedToOneToFifty(int limit, int expected)
    {
        // Act
        await RunAsync(new { limit });

        // Assert
        Assert.That(_h.ItemFinder.Calls.Single().Query.Limit, Is.EqualTo(expected));
    }

    [Test]
    public async Task FindItems_ReturnsTheFoundItemsWithAssigneeCreatorAndHasMore()
    {
        // Arrange
        _h.ItemFinder.OnFind = _ => new ItemFindResult(
            [
                new FoundItem(
                    "WEB-7", "WEB", "網站改版", "做首頁", "task", "doing", "in_progress", new DateOnly(2026, 12, 1), "high",
                    new FoundPerson(Afang, "阿芳"), new FoundPerson(_h.Actor.PersonId, "小明")),
                new FoundItem("OPS-1", "OPS", "維運", "巡檢", "task", "todo", "not_started", null, "none", null, new FoundPerson(Afang, "阿芳")),
            ],
            true);

        // Act
        var result = await RunAsync(new { });

        // Assert
        var first = result.Result.GetProperty("items")[0];
        var second = result.Result.GetProperty("items")[1];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.Result.GetProperty("notice").GetString(), Does.Contain("使用者寫的內容"));
            Assert.That(result.Result.GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(first.GetProperty("key").GetString(), Is.EqualTo("WEB-7"));
            Assert.That(first.GetProperty("projectName").GetString(), Is.EqualTo("網站改版"));
            Assert.That(first.GetProperty("stateCategory").GetString(), Is.EqualTo("in_progress"));
            Assert.That(first.GetProperty("dueDate").GetString(), Is.EqualTo("2026-12-01"));
            Assert.That(first.GetProperty("priority").GetString(), Is.EqualTo("high"));
            Assert.That(first.GetProperty("assignee").GetProperty("name").GetString(), Is.EqualTo("阿芳"));
            Assert.That(first.GetProperty("createdBy").GetProperty("name").GetString(), Is.EqualTo("小明"));
            Assert.That(second.GetProperty("assignee").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(second.GetProperty("dueDate").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
    }

    [TestCase("""{"assignee":"阿芳"}""")]
    [TestCase("""{"createdBy":"everyone"}""")]
    [TestCase("""{"stateCategory":["todo"]}""")]
    [TestCase("""{"stateCategory":["done",3]}""")]
    [TestCase("""{"dueFrom":"下週"}""")]
    [TestCase("""{"dueTo":"2026-02-30"}""")]
    [TestCase("""{"overdue":true,"stateCategory":["done"]}""")]
    [TestCase("""{"overdue":true,"stateCategory":"canceled"}""")]
    [TestCase("{\"text\":\"a\\u0000b\"}")]
    [TestCase("{\"projectKey\":\"WEB\\n\"}")]
    [TestCase("{\"assignee\":\"me\\r\"}")]
    public async Task FindItems_BadArguments_AreAToolErrorAndNeverReachTheFinder(string json)
    {
        // Act
        var result = await RunAsync(JsonDocument.Parse(json).RootElement);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.Result.GetProperty("error").GetString(), Is.Not.Empty);
            Assert.That(_h.ItemFinder.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task FindItems_TextLongerThanTheLimit_IsAToolError()
    {
        // Act
        var result = await RunAsync(new { text = new string('字', AiRules.MaxFindItemsTextLength + 1) });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(_h.ItemFinder.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task FindItems_FinderFailure_IsReportedAsAToolError()
    {
        // Arrange
        _h.ItemFinder.OnFind = _ => WorkSamples.Failure(FailureKind.NotFound, "project.not_found", "找不到這個專案。");

        // Act
        var result = await RunAsync(new { projectKey = "NOPE" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.Result.GetProperty("error").GetString(), Is.EqualTo("找不到這個專案。"));
        }
    }

    [Test]
    public async Task ListPeople_InAnOrganizationConversation_WorksWithoutAProject()
    {
        // Arrange
        _h.People.Set(_h.OrganizationId, Afang, PmTool.Identity.OrganizationRole.Member, name: "阿芳");
        _h.People.Set(_h.OrganizationId, _h.Actor.PersonId, PmTool.Identity.OrganizationRole.Member, name: "小明");
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("call-1", "list_people", new { text = "阿芳" })).Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id, "阿芳的編號");

        // Assert
        var result = LastResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("people")[0].GetProperty("name").GetString(), Is.EqualTo("阿芳"));
            Assert.That(result.GetProperty("people")[0].GetProperty("projectRole").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(_h.Projects.Gets, Is.Empty);
        }
    }

    [Test]
    public void ToolLists_FollowTheAgentVersion()
    {
        // Act
        var oldProject = AgentToolLists.For("WEB", "0.18.0");
        var newProject = AgentToolLists.For("WEB", AiRules.MinAgentVersionForFindItems);
        var oldOrganization = AgentToolLists.For(null, "0.18.0");
        var newOrganization = AgentToolLists.For(null, AiRules.MinAgentVersionForFindItems);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldProject, Does.Not.Contain("find_items").And.Contain("my_work"));
            Assert.That(newProject, Does.Contain("find_items"));
            Assert.That(oldOrganization, Does.Not.Contain("find_items"));
            Assert.That(newOrganization, Does.Contain("find_items").And.Contain("list_people"));
            Assert.That(AgentToolLists.SupportsFindItems("0.18.0"), Is.False);
            Assert.That(AgentToolLists.SupportsFindItems(AiRules.MinAgentVersionForFindItems), Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AgentPrompts_ForAKumiThatIsTooOld_DropFindItemsAndSayToUpdate(bool host)
    {
        // Act
        var project = host
            ? LocalPromptBuilder.HostSystemPrompt("WEB", [], supportsFindItems: false)
            : LocalPromptBuilder.SystemPrompt("WEB", [], supportsFindItems: false);
        var organization = host
            ? LocalPromptBuilder.HostOrganizationSystemPrompt(true, supportsFindItems: false)
            : LocalPromptBuilder.OrganizationSystemPrompt(true, supportsFindItems: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var prompt in new[] { project, organization })
            {
                Assert.That(prompt, Does.Not.Contain("- find_items："));
                Assert.That(prompt, Does.Contain("這一次沒有 find_items 這個工具").And.Contain("kumi"));
                Assert.That(prompt, Does.Contain("- my_work："));
            }

            Assert.That(organization, Does.Not.Contain("- list_people："));
            Assert.That(project, Does.Contain("- list_people："));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AgentPrompts_ForACurrentKumi_ListFindItemsWithoutAnUpdateNote(bool host)
    {
        // Act
        var project = host ? LocalPromptBuilder.HostSystemPrompt("WEB", []) : LocalPromptBuilder.SystemPrompt("WEB", []);
        var organization = host ? LocalPromptBuilder.HostOrganizationSystemPrompt(true) : LocalPromptBuilder.OrganizationSystemPrompt(true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var prompt in new[] { project, organization })
            {
                Assert.That(prompt, Does.Contain("- find_items："));
                Assert.That(prompt, Does.Not.Contain("這一次沒有 find_items"));
            }

            Assert.That(organization, Does.Contain("- list_people："));
        }
    }

    [Test]
    public void SystemPrompts_TellTheModelToUseFindItemsForOthersWork()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AssistantPrompt.SystemPrompt, Does.Contain("find_items").And.Contain("看不到的項目就是查不到"));
            Assert.That(AssistantPrompt.OrganizationSystemPrompt, Does.Contain("find_items").And.Contain("list_people"));
            Assert.That(AssistantPrompt.OrganizationSystemPrompt, Does.Not.Contain("別人的項目讀不到"));
            Assert.That(AssistantPrompt.SystemPrompt, Does.Not.Contain("別人的項目讀不到"));
        }
    }

    private async Task<(Guid ConversationId, JsonElement Result)> RunAsync(object arguments, bool organization = false)
    {
        var conversation = organization ? await _h.NewOrganizationConversationAsync() : await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("call-1", "find_items", arguments)).Reply("好");
        if (organization)
        {
            await _h.SendOrganizationAsync(conversation.Id, "阿芳下週有哪些工作");
        }
        else
        {
            await _h.SendAsync(conversation.Id);
        }

        return (conversation.Id, LastResult());
    }

    private JsonElement LastResult()
    {
        var result = _h.Script.Calls.Last().Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
        using var document = JsonDocument.Parse((string)result.Result!);
        return document.RootElement.Clone();
    }
}
