using System.Text.Json;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Blueprint.Planning;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>六個工具：模型看到的結果（成功 ok:true、預期中的失敗 ok:false 加原因與問題）、權限與送進工作引擎的內容。</summary>
[TestFixture]
public sealed class AssistantToolTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task GetBlueprint_ReturnsVersionBlueprintAndWhatTheUserMayDo()
    {
        // Act
        var result = await RunToolAsync("get_blueprint");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("version").GetInt64(), Is.EqualTo(3));
            Assert.That(result.GetProperty("blueprint").GetProperty("metadata").GetProperty("name").GetString(), Is.EqualTo("網站專案"));
            Assert.That(result.GetProperty("canPropose").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("canApply").GetBoolean(), Is.True);
        }
    }

    [Test]
    public async Task GetBlueprint_UserWhoCannotPropose_SaysSo()
    {
        // Arrange
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail([WorkPermissions.ItemRead, WorkPermissions.BlueprintRead]);

        // Act
        var result = await RunToolAsync("get_blueprint");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("canPropose").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("canApply").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task GetBlueprint_ProjectFailure_IsOkFalseWithTheMessage()
    {
        // Arrange：回覆開始之後專案被封存或人被移出，工具再讀就失敗。
        var calls = 0;
        _h.Projects.OnGet = (_, _) => ++calls <= 2
            ? WorkSamples.Detail()
            : WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案「WEB」。");

        // Act
        var result = await RunToolAsync("get_blueprint");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Is.EqualTo("找不到專案「WEB」。"));
        }
    }

    [Test]
    public async Task ListTemplates_ListsKeyNameAndTheirShape()
    {
        // Act
        var result = await RunToolAsync("list_templates");

        // Assert
        var template = result.GetProperty("templates")[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(template.GetProperty("key").GetString(), Is.EqualTo("simple_tasks"));
            Assert.That(template.GetProperty("name").GetString(), Is.EqualTo("簡單任務"));
            Assert.That(template.GetProperty("itemTypes")[0].GetString(), Is.EqualTo("任務"));
            Assert.That(template.GetProperty("workflows")[0].GetProperty("states")[1].GetString(), Is.EqualTo("完成"));
            Assert.That(template.GetProperty("workflows")[0].GetProperty("canceledStates")[0].GetString(), Is.EqualTo("取消"));
            Assert.That(template.GetProperty("fields")[0].GetString(), Is.EqualTo("分類"));
        }
    }

    [Test]
    public async Task GetTemplate_KnownKey_ReturnsTheBlueprint()
    {
        // Act
        var result = await RunToolAsync("get_template", new { templateKey = "simple_tasks" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("key").GetString(), Is.EqualTo("simple_tasks"));
            Assert.That(result.GetProperty("blueprint").GetProperty("itemTypes")[0].GetProperty("key").GetString(), Is.EqualTo("task"));
        }
    }

    [TestCase("nope")]
    [TestCase("")]
    public async Task GetTemplate_UnknownOrMissingKey_IsOkFalse(string key)
    {
        // Act
        var result = await RunToolAsync("get_template", new { templateKey = key });
        var withoutArgument = await RunToolAsync("get_template");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(withoutArgument.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(withoutArgument.GetProperty("error").GetString(), Does.Contain("templateKey"));
        }
    }

    [Test]
    public async Task DescribeUsage_ReturnsOnlyNumbers()
    {
        // Act
        var result = await RunToolAsync("describe_usage");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("itemCount").GetInt64(), Is.EqualTo(5));
            Assert.That(result.GetProperty("states")[0].GetProperty("state").GetString(), Is.EqualTo("todo"));
            Assert.That(result.GetProperty("fields")[0].GetProperty("filledItems").GetInt64(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task DescribeUsage_Forbidden_IsOkFalseWithTheReason()
    {
        // Arrange
        _h.Usage.OnDescribe = () => WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "要看得到專案裡所有的項目。");

        // Act
        var result = await RunToolAsync("describe_usage");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Is.EqualTo("要看得到專案裡所有的項目。"));
        }
    }

    [Test]
    public async Task PreviewChangeSet_SendsTheObjectAsJson_AndShapesChangesImpactsAndBlockers()
    {
        // Arrange
        _h.Changes.OnPreview = _ => new ChangeSetPreview(
            3, 4, WorkSamples.Blueprint,
            [new BlueprintChange { Kind = ChangeKind.Modified, Resource = ResourceKind.State, Key = "todo", ParentKey = "flow", ChangedProperties = ["name"] }],
            [
                new PlannedImpact(new StateMoveImpact("flow", "todo", "doing"), 7, BlocksApply: false),
                new PlannedImpact(new ItemTypeRemovalImpact("bug"), 2, BlocksApply: true),
                new PlannedImpact(new FieldDropImpact("category"), 1, BlocksApply: false),
            ],
            [new FailureDetail("changeset.base_version_stale", "baseVersion", "基準版本已經過期")],
            CanApply: false);

        // Act
        var result = await RunToolAsync("preview_changeset", new { changeset = new { baseVersion = 3, summary = "改名" } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonDocument.Parse(_h.Changes.Previews.Single()).RootElement.GetProperty("summary").GetString(), Is.EqualTo("改名"));
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("baseVersion").GetInt64(), Is.EqualTo(3));
            Assert.That(result.GetProperty("currentVersion").GetInt64(), Is.EqualTo(4));
            Assert.That(result.GetProperty("canApply").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("changes")[0].GetProperty("kind").GetString(), Is.EqualTo("modified"));
            Assert.That(result.GetProperty("changes")[0].GetProperty("resource").GetString(), Is.EqualTo("state"));
            Assert.That(result.GetProperty("changes")[0].GetProperty("changedProperties")[0].GetString(), Is.EqualTo("name"));
            Assert.That(result.GetProperty("impacts")[0].GetProperty("kind").GetString(), Is.EqualTo("state_move"));
            Assert.That(result.GetProperty("impacts")[0].GetProperty("affected").GetInt64(), Is.EqualTo(7));
            Assert.That(result.GetProperty("impacts")[0].GetProperty("to").GetString(), Is.EqualTo("doing"));
            Assert.That(result.GetProperty("impacts")[1].GetProperty("blocksApply").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("impacts")[1].GetProperty("itemType").GetString(), Is.EqualTo("bug"));
            Assert.That(result.GetProperty("impacts")[2].GetProperty("kind").GetString(), Is.EqualTo("field_drop"));
            Assert.That(result.GetProperty("blockers")[0].GetProperty("code").GetString(), Is.EqualTo("changeset.base_version_stale"));
        }
    }

    [Test]
    public async Task PreviewChangeSet_ChangeSetSentAsAJsonString_IsAccepted()
    {
        // Act
        var result = await RunToolAsync("preview_changeset", new { changeset = """{"baseVersion":3}""" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(_h.Changes.Previews.Single(), Is.EqualTo("""{"baseVersion":3}"""));
        }
    }

    [TestCase("not json")]
    [TestCase("[1,2]")]
    [TestCase("")]
    public async Task PreviewChangeSet_NotAnObject_IsOkFalseAndNeverReachesTheEngine(string changeset)
    {
        // Act
        var result = await RunToolAsync("preview_changeset", new { changeset });
        var missing = await RunToolAsync("preview_changeset");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(missing.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(_h.Changes.Previews, Is.Empty);
        }
    }

    [Test]
    public async Task PreviewChangeSet_InvalidDocument_ListsEveryIssueForTheModelToFix()
    {
        // Arrange
        _h.Changes.OnPreview = _ => WorkSamples.Failure(
            FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid, "變更集有 2 個問題",
            new FailureDetail("blueprint.duplicate_key", "blueprint.fields[1].key", "key 重複"),
            new FailureDetail("changeset.missing_summary", "summary", "缺少 summary"));

        // Act
        var result = await RunToolAsync("preview_changeset", new { changeset = new { baseVersion = 3 } });

        // Assert
        var issues = result.GetProperty("issues");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Is.EqualTo("變更集驗證不過"));
            Assert.That(issues.GetArrayLength(), Is.EqualTo(2));
            Assert.That(issues[0].GetProperty("code").GetString(), Is.EqualTo("blueprint.duplicate_key"));
            Assert.That(issues[0].GetProperty("path").GetString(), Is.EqualTo("blueprint.fields[1].key"));
            Assert.That(issues[1].GetProperty("message").GetString(), Is.EqualTo("缺少 summary"));
        }
    }

    [Test]
    public async Task ProposeChangeSet_ProposesAsTheAssistantForTheStartingPerson()
    {
        // Act
        var result = await RunToolAsync("propose_changeset", new { changeset = new { baseVersion = 3, summary = "改名" } });

        // Assert
        var proposal = _h.Changes.Proposals.Single();
        var changeSet = result.GetProperty("changeSet");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposal.Origin, Is.EqualTo(ChangeSetOrigin.Assistant));
            Assert.That(proposal.Actor, Is.EqualTo(_h.Actor));
            Assert.That(proposal.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(proposal.Format, Is.EqualTo(PmTool.Blueprint.DocumentFormat.Json));
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(changeSet.GetProperty("number").GetInt64(), Is.EqualTo(12));
            Assert.That(changeSet.GetProperty("url").GetString(), Is.EqualTo("/projects/WEB/changesets/12"));
            Assert.That(changeSet.GetProperty("canApply").GetBoolean(), Is.True);
        }
    }

    [Test]
    public async Task ProposeChangeSet_SecondCallInTheSameReply_IsAToolErrorTheModelCanRead()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        var arguments = new { changeset = new { baseVersion = 3, summary = "改名" } };
        _h.Script
            .Enqueue(Turn.Create().Call("p1", "propose_changeset", arguments))
            .Enqueue(Turn.Create().Call("p2", "propose_changeset", arguments))
            .Reply("好");

        // Act
        await _h.SendAsync(conversation.Id);

        // Assert
        var results = _h.Script.Calls.Last().Messages
            .SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
        using var second = JsonDocument.Parse((string)results.Last().Result!);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_h.Changes.Proposals, Has.Count.EqualTo(1));
            Assert.That(second.RootElement.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(second.RootElement.GetProperty("error").GetString(), Does.Contain("最多只能提出 1 份"));
        }
    }

    [Test]
    public async Task ProposeChangeSet_TwoCallsAtOnce_OnlyOneReachesTheService()
    {
        // Arrange：第一個呼叫卡在服務裡，第二個同時進來。
        using var gate = new ManualResetEventSlim();
        _h.Changes.OnPropose = _ =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return WorkSamples.ProposedDetail(12);
        };
        var tool = NewProposeTool();
        var arguments = new AIFunctionArguments { ["changeset"] = JsonDocument.Parse("""{"baseVersion":3,"summary":"改名"}""").RootElement };

        // Act
        var first = Task.Run(async () => (string)(await tool.InvokeAsync(arguments))!);
        while (_h.Changes.Proposals.Count == 0)
        {
            await Task.Delay(10);
        }

        var second = (string)(await tool.InvokeAsync(arguments))!;
        gate.Set();
        var firstResult = await first;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_h.Changes.Proposals, Has.Count.EqualTo(1));
            Assert.That(second, Does.Contain("最多只能提出 1 份"));
            Assert.That(firstResult, Does.Contain("\"ok\":true"));
        }
    }

    [Test]
    public async Task ProposeChangeSet_AFailedAttempt_GivesTheSlotBack()
    {
        // Arrange
        var attempts = 0;
        _h.Changes.OnPropose = _ => ++attempts == 1
            ? WorkSamples.Failure(FailureKind.Conflict, WorkErrorCodes.ChangeSetOutdated, "版本太舊。")
            : WorkSamples.ProposedDetail(12);
        var tool = NewProposeTool();
        var arguments = new AIFunctionArguments { ["changeset"] = JsonDocument.Parse("""{"baseVersion":3,"summary":"改名"}""").RootElement };

        // Act
        var failed = (string)(await tool.InvokeAsync(arguments))!;
        var retried = (string)(await tool.InvokeAsync(arguments))!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed, Does.Contain("\"ok\":false"));
            Assert.That(retried, Does.Contain("\"ok\":true"));
        }
    }

    private AIFunction NewProposeTool() => new PmTool.Ai.Assistant.Tools.ProposeChangeSetTool(_h.NewToolContext());

    [Test]
    public async Task ProposeChangeSet_WithoutProposePermission_SaysSoInPlainWords()
    {
        // Arrange
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail([WorkPermissions.ItemRead, WorkPermissions.BlueprintRead]);
        _h.Changes.OnPropose = _ => WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "你在這個專案沒有提出設定修改的權限。");

        // Act
        var result = await RunToolAsync("propose_changeset", new { changeset = new { baseVersion = 3 } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Is.EqualTo("你沒有提出變更集的權限。"));
        }
    }

    [Test]
    public async Task ProposeChangeSet_ForbiddenForAnotherReason_KeepsTheEnginesMessage()
    {
        // Arrange
        _h.Changes.OnPropose = _ => WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "要看得到專案裡所有的項目。");

        // Act
        var result = await RunToolAsync("propose_changeset", new { changeset = new { baseVersion = 3 } });

        // Assert
        Assert.That(result.GetProperty("error").GetString(), Is.EqualTo("要看得到專案裡所有的項目。"));
    }

    [Test]
    public async Task ProposeChangeSet_InvalidDocument_IsOkFalseWithIssues_AndShowsAsAFailedToolPart()
    {
        // Arrange
        _h.Changes.OnPropose = _ => WorkSamples.Failure(
            FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid, "變更集有問題",
            new FailureDetail("changeset.stale", "baseVersion", "版本過期"));
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Call("p", "propose_changeset", new { changeset = new { baseVersion = 1 } }))
            .Reply("版本過期，我重新讀。");

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Status, Is.EqualTo(AssistantToolStatus.Failed));
            Assert.That(tool.Error, Is.EqualTo("變更集驗證不過"));
            Assert.That(tool.ChangeSetNumber, Is.Null);
        }
    }

    [Test]
    public async Task Tool_ThrowingUnexpectedly_IsOkFalseWithoutLeakingTheException()
    {
        // Arrange
        _h.Usage.OnDescribe = () => throw new InvalidOperationException("連線字串 Host=secret-db;Password=hunter2");

        // Act
        var result = await RunToolAsync("describe_usage");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetRawText(), Does.Not.Contain("hunter2"));
        }
    }

    [Test]
    public async Task Tool_ResultLongerThanTheLimit_IsCutButStaysValidJsonWithItsOkFlag()
    {
        // Arrange
        _h.Usage.OnDescribe = () => new BlueprintUsage(
            3, 1, [], [.. Enumerable.Range(0, 4000).Select(i => new FieldUsage($"field_with_a_long_name_{i:D5}", i))]);

        // Act
        var result = await RunToolAsync("describe_usage");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("partial").GetString()!.Length, Is.LessThan(AiRules.MaxToolResultLength));
            Assert.That(result.GetRawText().Length, Is.LessThanOrEqualTo(AiRules.MaxToolResultLength * 2));
        }
    }

    [Test]
    public async Task Tool_UnknownName_ShowsAsAFailedToolPart()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("x", "delete_everything")).Reply("沒有這個工具。");

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Name, Is.EqualTo("delete_everything"));
            Assert.That(tool.Status, Is.EqualTo(AssistantToolStatus.Failed));
            Assert.That(tool.Error, Is.Not.Empty);
        }
    }

    [Test]
    public void ToolSchemas_UseOnlyTheAllowedKeywords()
    {
        // Arrange
        var allowed = new HashSet<string> { "type", "properties", "required", "additionalProperties", "description", "items", "enum", "const", "$ref" };
        var context = _h.NewToolContext();

        // Act
        var tools = PmTool.Ai.Assistant.Tools.AssistantToolSet.Create(context).OfType<AIFunction>().ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools, Has.Count.EqualTo(26));
            foreach (var tool in tools)
            {
                Assert.That(KeywordsOf(tool.JsonSchema).Except(allowed), Is.Empty, tool.Name);
                Assert.That(tool.JsonSchema.GetProperty("additionalProperties").GetBoolean(), Is.False, tool.Name);
            }
        }
    }

    private static IEnumerable<string> KeywordsOf(JsonElement schema)
    {
        // properties 底下的名稱是參數名，不是關鍵字；其他物件逐層看。
        foreach (var property in schema.EnumerateObject())
        {
            yield return property.Name;
            if (property.Name == "properties")
            {
                foreach (var parameter in property.Value.EnumerateObject())
                {
                    foreach (var inner in KeywordsOf(parameter.Value))
                    {
                        yield return inner;
                    }
                }
            }
        }
    }

    /// <summary>讓模型呼叫一個工具，回傳下一次呼叫模型時帶上的那個工具結果（也就是模型看到的）。</summary>
    private async Task<JsonElement> RunToolAsync(string tool, object? arguments = null)
    {
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("call-1", tool, arguments)).Reply("好");
        await _h.SendAsync(conversation.Id);
        var result = _h.Script.Calls.Last().Messages
            .SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
        using var document = JsonDocument.Parse((string)result.Result!);
        return document.RootElement.Clone();
    }
}
