using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 送出時的檢查與套用前的把關：YAML 和 JSON 一樣收；格式、語意、計畫有問題就不存；
/// 別的變更集套用之後，舊的等待核准的變更集過期；資料擋住的變更集套用不了，什麼都不會變。
/// </summary>
[TestFixture]
public sealed class ChangeSetReviewTests : RealStackTestBase
{
    // 「簡單任務看板」的藍圖全文，metadata 是 YAML 這個專案的，只多了一個「審查中」狀態。CLI 的使用者就是這樣寫的。
    private const string AddReviewYaml = """
        baseVersion: 1
        summary: 加上審查中
        blueprint:
          apiVersion: blueprint/v0
          kind: ProjectBlueprint
          metadata:
            key: yaml
            name: Example YAML
            description: 待辦、進行中、完成三個狀態的任務看板，適合小團隊追蹤日常工作。
            basedOn: { template: simple_tasks, version: "1.0.0" }
          fields:
            - key: category
              name: 分類
              type: single_select
              options:
                - { key: feature, name: 新功能, color: blue }
                - { key: bug,     name: 錯誤,   color: red }
                - { key: chore,   name: 雜務,   color: gray }
            - key: tags
              name: 標籤
              type: multi_select
              options:
                - { key: frontend, name: 前端, color: purple }
                - { key: backend,  name: 後端, color: green }
                - { key: docs,     name: 文件, color: yellow }
            - { key: review_date,   name: 檢查日,   type: date }
            - { key: story_points,  name: 點數,     type: number }
            - { key: budget,        name: 預算,     type: currency, currency: TWD }
            - { key: blocked,       name: 卡住了,   type: checkbox }
            - { key: reference_url, name: 參考連結, type: url }
            - { key: reviewer,      name: 審查者,   type: person }
            - { key: summary,       name: 摘要,     type: text }
            - { key: notes,         name: 備註,     type: long_text }
          workflows:
            - key: task_flow
              name: 任務流程
              states:
                - { key: todo,   name: 待辦,   category: not_started }
                - { key: doing,  name: 進行中, category: in_progress }
                - { key: review, name: 審查中, category: in_progress }
                - { key: done,   name: 完成,   category: done }
          itemTypes:
            - key: task
              name: 任務
              workflow: task_flow
              fields: [category, tags, review_date, story_points, budget, blocked, reference_url, reviewer, summary, notes]
          views:
            - { key: board, name: 看板, type: board, groupBy: state }
            - key: all_tasks
              name: 所有任務
              type: table
              columns: [title, state, assignee, category, tags, due_date]
              sort: [{ field: due_date, direction: asc }]
        """;

    [Test]
    public async Task Propose_YamlDocument_IsAcceptedAndPlannedLikeJson()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "YAML", "simple_tasks");

        // Act
        var proposed = await ChangeSetSteps.ProposedAsync(owner.Client, "YAML", ApiRequests.Document(AddReviewYaml, "application/yaml"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ChangeSetSteps.Describe(proposed["changeSet"]), Is.EqualTo("#1 pending base=1 applied=null by=Owner Example decidedBy=null"));
            Assert.That(proposed["changeSet"]?["summary"]?.GetValue<string>(), Is.EqualTo("加上審查中"));
            Assert.That(ChangeSetSteps.DescribeChanges(proposed["changes"]), Is.EqualTo(new[]
            {
                "added|state|review|task_flow|",
            }));
            Assert.That(proposed["impacts"]?.AsArray(), Is.Empty);
            Assert.That(proposed["blockers"]?.AsArray(), Is.Empty);
            Assert.That(proposed["canApply"]?.GetValue<bool>(), Is.True);
            Assert.That(ChangeSetSteps.StateKeysOf(proposed["blueprint"], "task_flow"), Is.EqualTo(new[] { "todo", "doing", "review", "done" }));
        }
    }

    [Test]
    public async Task Propose_DocumentWithSyntaxOrMeaningProblems_IsRejectedWithEveryIssueAndNotStored()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "BAD", "simple_tasks");
        var withoutDoing = ChangeSetSteps.WithoutState(await ChangeSetSteps.BlueprintOfAsync(owner.Client, "BAD"), "task_flow", "doing");
        var toMissingState = new JsonObject { ["moveStates"] = new JsonArray(ChangeSetSteps.MoveState("task_flow", "doing", "archived")) };

        // Act
        using var syntax = await ChangeSetSteps.ProposeAsync(owner.Client, "BAD", ApiRequests.Document("baseVersion: 1\nblueprint: [", "application/yaml"));
        using var unknownTarget = await ChangeSetSteps.ProposeAsync(owner.Client, "BAD", ChangeSetSteps.Json(1, "搬去不存在的狀態", withoutDoing, toMissingState));
        using var missingMove = await ChangeSetSteps.ProposeAsync(owner.Client, "BAD", ChangeSetSteps.Json(1, "沒寫搬去哪裡", withoutDoing));
        var list = await RealStackSteps.GetAsync(owner.Client, ChangeSetSteps.RouteOf("BAD"));

        // Assert
        var syntaxIssues = await ApiRequests.AssertFailureEnvelopeAsync(syntax, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ChangeSetInvalid);
        var targetIssues = await ApiRequests.AssertFailureEnvelopeAsync(unknownTarget, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ChangeSetInvalid);
        var moveIssues = await ApiRequests.AssertFailureEnvelopeAsync(missingMove, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ChangeSetInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(syntaxIssues.Select(i => i?["code"]?.GetValue<string>()), Is.EqualTo(new[] { IssueCodes.DocumentSyntax }));
            Assert.That(ApiRequests.Describe(targetIssues), Is.EqualTo(new[] { $"{IssueCodes.ChangeSetMoveUnknownTarget}|/migrations/moveStates/0/to" }));
            Assert.That(ApiRequests.Describe(moveIssues), Is.EqualTo(new[] { $"{IssueCodes.PlanStateMoveRequired}|/migrations/moveStates" }));
            Assert.That(moveIssues[0]?["message"]?.GetValue<string>(), Does.Contain("「進行中」").And.Contain("「任務流程」"), "訊息用名稱，不用 key");
            Assert.That(list.AsArray(), Is.Empty, "沒通過檢查的不存");
        }
    }

    [Test]
    public async Task Propose_AgainstAnOutdatedVersion_IsRejectedAndTheNextOneKeepsTheNumbering()
    {
        // Arrange：先套用一份，專案的藍圖變成第 2 版。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "STALE", "simple_tasks");
        var first = await ChangeSetSteps.BlueprintOfAsync(owner.Client, "STALE");
        await ChangeSetSteps.ProposedAsync(owner.Client, "STALE", ChangeSetSteps.Json(
            1, "加上審查中", ChangeSetSteps.WithState(first, "task_flow", "review", "審查中", "in_progress")));
        await ChangeSetSteps.AppliedAsync(owner.Client, "STALE", 1);
        var second = await ChangeSetSteps.BlueprintOfAsync(owner.Client, "STALE");
        var withEstimate = ChangeSetSteps.WithField(second, "task", "effort_hours", "工時", "number");

        // Act
        using var outdated = await ChangeSetSteps.ProposeAsync(owner.Client, "STALE", ChangeSetSteps.Json(1, "照第 1 版寫的", withEstimate));
        var current = await ChangeSetSteps.ProposedAsync(owner.Client, "STALE", ChangeSetSteps.Json(2, "照第 2 版寫的", withEstimate));

        // Assert
        var issues = await ApiRequests.AssertFailureEnvelopeAsync(outdated, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ChangeSetInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(issues), Is.EqualTo(new[] { $"{IssueCodes.PlanBaseVersionMismatch}|/baseVersion" }));
            Assert.That(ChangeSetSteps.Describe(current["changeSet"]), Is.EqualTo("#2 pending base=2 applied=null by=Owner Example decidedBy=null"), "沒存的不占編號");
            Assert.That(ChangeSetSteps.DescribeChanges(current["changes"]), Is.EqualTo(new[]
            {
                "added|field|effort_hours||",
                "modified|item_type|task||fields",
            }));
        }
    }

    [Test]
    public async Task Apply_OneOfTwoPendingChangeSets_ExpiresTheOtherSoItCanNoLongerBeAppliedOrRejected()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "EXPIRE", "simple_tasks");
        var blueprint = await ChangeSetSteps.BlueprintOfAsync(owner.Client, "EXPIRE");
        await ChangeSetSteps.ProposedAsync(owner.Client, "EXPIRE", ChangeSetSteps.Json(
            1, "加上審查中", ChangeSetSteps.WithState(blueprint, "task_flow", "review", "審查中", "in_progress")));
        await ChangeSetSteps.ProposedAsync(owner.Client, "EXPIRE", ChangeSetSteps.Json(
            1, "加上工時", ChangeSetSteps.WithField(blueprint, "task", "effort_hours", "工時", "number")));

        // Act
        await ChangeSetSteps.AppliedAsync(owner.Client, "EXPIRE", 1);
        var other = await ChangeSetSteps.GetAsync(owner.Client, "EXPIRE", 2);
        using var apply = await ChangeSetSteps.ApplyAsync(owner.Client, "EXPIRE", 2);
        using var reject = await ChangeSetSteps.RejectAsync(owner.Client, "EXPIRE", 2);
        var list = await RealStackSteps.GetAsync(owner.Client, ChangeSetSteps.RouteOf("EXPIRE"));
        var project = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/EXPIRE");

        // Assert：過期是在套用第 1 份的同一個交易裡標好的，所以之後看到的是「不是等待核准」，不是「基準版本過時」。
        await ApiRequests.AssertFailureEnvelopeAsync(apply, HttpStatusCode.Conflict, WorkErrorCodes.ChangeSetNotPending);
        await ApiRequests.AssertFailureEnvelopeAsync(reject, HttpStatusCode.Conflict, WorkErrorCodes.ChangeSetNotPending);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ChangeSetSteps.Describe(other["changeSet"]), Is.EqualTo("#2 expired base=1 applied=null by=Owner Example decidedBy=null"));
            Assert.That(other["changeSet"]?["decidedAt"]?.GetValue<DateTimeOffset>(), Is.Not.Null);
            Assert.That(other["canApply"]?.GetValue<bool>(), Is.False);
            Assert.That(other["blockers"]?.AsArray(), Is.Empty);
            Assert.That(list.AsArray().Select(ChangeSetSteps.Describe), Is.EqualTo(new[]
            {
                "#2 expired base=1 applied=null by=Owner Example decidedBy=null",
                "#1 applied base=1 applied=2 by=Owner Example decidedBy=Owner Example",
            }));
            Assert.That(project["project"]?["blueprintVersion"]?.GetValue<long>(), Is.EqualTo(2));
            Assert.That(ChangeSetSteps.FieldsOfType(project["blueprint"], "task"), Does.Not.Contain("effort_hours"), "過期的那份沒有套用");
        }
    }

    [Test]
    public async Task Apply_RemovingAnItemTypeThatStillHasItems_IsBlockedAndChangesNothing()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "BLOCK", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "BLOCK", new { type = "requirement", title = "首頁改版需求" });
        await RealStackSteps.CreateItemAsync(owner.Client, "BLOCK", new { type = "change_request", title = "首頁多一個橫幅" });
        var blueprint = ChangeSetSteps.WithoutItemType(await ChangeSetSteps.BlueprintOfAsync(owner.Client, "BLOCK"), "change_request");

        // Act
        var proposed = await ChangeSetSteps.ProposedAsync(owner.Client, "BLOCK", ChangeSetSteps.Json(1, "不再收需求變更單", blueprint));
        using var apply = await ChangeSetSteps.ApplyAsync(owner.Client, "BLOCK", 1);
        var afterwards = await ChangeSetSteps.GetAsync(owner.Client, "BLOCK", 1);
        var project = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/BLOCK");
        var item = await RealStackSteps.GetAsync(owner.Client, "/v1/items/BLOCK-2");
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "BLOCK-2");

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(apply, HttpStatusCode.Conflict, WorkErrorCodes.ChangeSetApplyBlocked);
        var expectedImpacts = new JsonArray(new JsonObject
        {
            ["kind"] = "item_type_removal",
            ["affected"] = 1,
            ["blocksApply"] = true,
            ["itemType"] = "change_request",
        });
        string[] blocker = [$"{WorkErrorCodes.ChangeSetItemsOfRemovedType}|impacts/0"];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(proposed["impacts"], expectedImpacts), Is.True, proposed["impacts"]?.ToJsonString());
            Assert.That(ApiRequests.Describe(proposed["blockers"]!.AsArray()), Is.EqualTo(blocker));
            Assert.That(proposed["blockers"]?[0]?["message"]?.GetValue<string>(), Does.Contain("需求變更單").And.Contain("1 個項目"));
            Assert.That(proposed["canApply"]?.GetValue<bool>(), Is.False);
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(blocker));
            Assert.That(ChangeSetSteps.Describe(afterwards["changeSet"]), Is.EqualTo("#1 pending base=1 applied=null by=Owner Example decidedBy=null"));
            Assert.That(project["project"]?["blueprintVersion"]?.GetValue<long>(), Is.EqualTo(1));
            Assert.That(RealStackSteps.KeysOf(project["blueprint"]?["itemTypes"]), Is.EqualTo(new[] { "requirement", "change_request", "task", "subtask" }));
            Assert.That(item["type"]?.GetValue<string>(), Is.EqualTo("change_request"));
            Assert.That(RealStackSteps.KindsOf(activity["items"]), Is.EqualTo(new[] { "created" }));
        }
    }
}
