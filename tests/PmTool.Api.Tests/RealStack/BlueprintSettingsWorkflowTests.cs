using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 網頁設定頁第二片會送出的變更集（轉換與類型）：形狀照網頁組的——migrations 一定帶兩個陣列，
/// 有限制的流程每條轉換都帶 allowedRoles 與 requireFields，沒改的物件原樣帶回。對真的資料庫送出、套用，
/// 之後改狀態與項目的狀態照新的藍圖走。用「接案專案」：交付流程有列轉換，需求與需求變更單用它，任務用沒有限制的任務流程。
/// </summary>
[TestFixture]
public sealed class BlueprintSettingsWorkflowTests : RealStackTestBase
{
    // 從需求確認走到客戶驗收要經過的狀態。客戶驗收要專案經理才能送。
    private static readonly string[] UpToClientQa = ["quoting", "designing", "developing", "internal_qa", "client_qa"];

    // 交付流程的每個狀態都寫一條搬移，和網頁一樣：舊流程有、新流程沒有的狀態各一條，依舊流程的順序。
    // 客戶驗收算完成：故意讓一個狀態換了類別，才看得出套用後的狀態類別是跟著新的狀態走的。
    private static readonly (string From, string To)[] ToTaskFlow =
    [
        ("confirming", "todo"), ("quoting", "todo"),
        ("designing", "doing"), ("developing", "doing"), ("internal_qa", "doing"),
        ("client_qa", "done"), ("closed", "done"),
    ];

    [Test]
    public async Task Apply_StateAddedBetweenDevelopingAndInternalQa_ItemsMustGoThroughTheNewState()
    {
        // Arrange：一個需求停在開發。自動化的條件與動作只提到開發與內部驗收，不受影響。摘要沒填，網頁就不送。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "REVIEW", "agency_project");
        var item = await CreateItemAtAsync(owner, "REVIEW", "requirement", "首頁改版需求", UpToClientQa[..3]);
        var wanted = ChangeSetSteps.WithTransitionReplaced(
            ChangeSetSteps.WithStateAfter(
                await ChangeSetSteps.BlueprintOfAsync(owner.Client, "REVIEW"), "delivery", "developing", "code_review", "程式審查", "in_progress"),
            "delivery",
            "developing",
            "internal_qa",
            ChangeSetSteps.Transition("developing", "code_review"),
            ChangeSetSteps.Transition("code_review", "internal_qa"));

        // Act
        var proposed = await ChangeSetSteps.ProposedAsync(owner.Client, "REVIEW", ChangeSetSteps.SettingsJson(1, null, wanted));
        await ChangeSetSteps.AppliedAsync(owner.Client, "REVIEW", 1);
        using var skipReview = await RealStackSteps.TransitionAsync(owner.Client, item, "internal_qa");
        var reviewed = await RealStackSteps.TransitionThroughAsync(owner.Client, item, "code_review", "internal_qa");
        var project = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/REVIEW");

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(skipReview, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.TransitionNotAllowed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ChangeSetSteps.DescribeChanges(proposed["changes"]), Is.EqualTo(new[]
            {
                "added|state|code_review|delivery|",
                "added|transition|developing->code_review|delivery|",
                "added|transition|code_review->internal_qa|delivery|",
                "removed|transition|developing->internal_qa|delivery|",
            }));
            Assert.That(RealStackSteps.IsWrittenAsNull(proposed["changeSet"], "summary"), Is.True, "沒有摘要時寫出 null");
            Assert.That(proposed["impacts"]?.AsArray(), Is.Empty);
            Assert.That(proposed["blockers"]?.AsArray(), Is.Empty);
            Assert.That(proposed["canApply"]?.GetValue<bool>(), Is.True);
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.TransitionNotAllowed}|state" }));
            Assert.That(details[0]?["message"]?.GetValue<string>(), Does.Contain("「程式審查」"), "說明可以轉去哪裡，用的是新的轉換");
            Assert.That($"{reviewed["state"]}|{reviewed["stateCategory"]}", Is.EqualTo("internal_qa|in_progress"));
            Assert.That(ChangeSetSteps.StateKeysOf(project["blueprint"], "delivery"), Is.EqualTo(new[]
            {
                "confirming", "quoting", "designing", "developing", "code_review", "internal_qa", "client_qa", "closed",
            }));
            Assert.That(ChangeSetSteps.DescribeTransitions(project["blueprint"], "delivery"), Is.EqualTo(new[]
            {
                "confirming->quoting|", "quoting->designing|", "designing->developing|",
                "developing->code_review|", "code_review->internal_qa|",
                "internal_qa->developing|", "internal_qa->client_qa|project_manager",
                "client_qa->developing|", "client_qa->closed|project_manager",
            }));
            Assert.That(RealStackSteps.KeysOf(project["blueprint"]?["automations"]), Is.EqualTo(new[] { "mr_merged_to_internal_qa" }));
        }
    }

    [Test]
    public async Task Apply_ChangeRequestsMovedToTheTaskFlow_PutsEachItemInItsMappedStateAndLeavesRequirementsAlone()
    {
        // Arrange：需求變更單停在需求確認、報價、開發、客戶驗收、結案各一張，需求停在開發。
        // 送到客戶驗收與結案要專案經理，結案還要客戶驗收日，所以先把自己改成專案經理。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "SWITCH", "agency_project");
        await RealStackSteps.SetMemberAsync(owner.Client, "SWITCH", owner.PersonId, "project_manager");
        await CreateItemAtAsync(owner, "SWITCH", "change_request", "首頁多一個橫幅");
        await CreateItemAtAsync(owner, "SWITCH", "change_request", "登入頁換文案", UpToClientQa[..1]);
        await CreateItemAtAsync(owner, "SWITCH", "change_request", "設定頁加深色模式", UpToClientQa[..3]);
        await CreateItemAtAsync(owner, "SWITCH", "change_request", "說明頁加影片", UpToClientQa);
        var closed = await CreateItemAtAsync(owner, "SWITCH", "change_request", "頁尾換版權宣告", UpToClientQa);
        await RealStackSteps.PatchItemAsync(owner.Client, closed, new { fields = new { client_signoff_date = "2026-11-30" } });
        await RealStackSteps.TransitionThroughAsync(owner.Client, closed, "closed");
        var requirement = await CreateItemAtAsync(owner, "SWITCH", "requirement", "首頁改版需求", UpToClientQa[..3]);
        var requirementBefore = await RealStackSteps.GetAsync(owner.Client, $"/v1/items/{requirement}");
        var wanted = ChangeSetSteps.WithItemTypeWorkflow(await ChangeSetSteps.BlueprintOfAsync(owner.Client, "SWITCH"), "change_request", "task_flow");
        var moves = ToTaskFlow.Select(move => ChangeSetSteps.MoveState("task_flow", move.From, move.To));

        // Act
        var proposed = await ChangeSetSteps.ProposedAsync(owner.Client, "SWITCH", ChangeSetSteps.SettingsJson(1, "需求變更單改用任務流程", wanted, moves));
        await ChangeSetSteps.AppliedAsync(owner.Client, "SWITCH", 1);
        var items = new List<JsonNode>();
        var lastActivities = new List<JsonNode?>();
        for (var number = 1; number <= 6; number++)
        {
            // 一個接一個讀：每一步的檢查用的範圍是跟著非同步流程走的，同時發出去會互相打架。
            items.Add(await RealStackSteps.GetAsync(owner.Client, $"/v1/items/SWITCH-{number}"));
            lastActivities.Add((await RealStackSteps.ActivityAsync(owner.Client, $"SWITCH-{number}"))["items"]!.AsArray()[^1]);
        }

        var straightToDone = await RealStackSteps.TransitionThroughAsync(owner.Client, "SWITCH-1", "done");

        // Assert
        var expectedImpacts = new JsonArray(
            StateMove("confirming", "todo", 1),
            StateMove("quoting", "todo", 1),
            StateMove("designing", "doing", 0),
            StateMove("developing", "doing", 1),
            StateMove("internal_qa", "doing", 0),
            StateMove("client_qa", "done", 1),
            StateMove("closed", "done", 1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ChangeSetSteps.DescribeChanges(proposed["changes"]), Is.EqualTo(new[] { "modified|item_type|change_request||workflow" }));
            Assert.That(JsonNode.DeepEquals(proposed["impacts"], expectedImpacts), Is.True, proposed["impacts"]?.ToJsonString());
            Assert.That(proposed["blockers"]?.AsArray(), Is.Empty);
            Assert.That(proposed["canApply"]?.GetValue<bool>(), Is.True);
            Assert.That(items.Select(i => $"{i["key"]}|{i["type"]}|{i["state"]}|{i["stateCategory"]}"), Is.EqualTo(new[]
            {
                "SWITCH-1|change_request|todo|not_started",
                "SWITCH-2|change_request|todo|not_started",
                "SWITCH-3|change_request|doing|in_progress",
                "SWITCH-4|change_request|done|done",
                "SWITCH-5|change_request|done|done",
                "SWITCH-6|requirement|developing|in_progress",
            }));
            Assert.That(lastActivities.Select(a => $"{a?["kind"]}|{a?["fromState"]}|{a?["toState"]}"), Is.EqualTo(new[]
            {
                "migrated|confirming|todo",
                "migrated|quoting|todo",
                "migrated|developing|doing",
                "migrated|client_qa|done",
                "migrated|closed|done",
                "transitioned|designing|developing",
            }));
            Assert.That(JsonNode.DeepEquals(items[5], requirementBefore), Is.True, "需求還在交付流程，一模一樣");
            Assert.That(straightToDone["stateCategory"]?.GetValue<string>(), Is.EqualTo("done"), "任務流程沒有限制，待辦可以直接到完成");
        }
    }

    /// <summary>新增一個項目，再依序轉過指定的狀態；回傳項目的編號。</summary>
    private static async Task<string> CreateItemAtAsync(SignedInClient owner, string projectKey, string type, string title, params string[] path)
    {
        var item = await RealStackSteps.CreateItemAsync(owner.Client, projectKey, new { type, title });
        var key = item["key"]!.GetValue<string>();
        if (path.Length > 0)
        {
            await RealStackSteps.TransitionThroughAsync(owner.Client, key, path);
        }

        return key;
    }

    private static JsonObject StateMove(string from, string to, long affected) => new()
    {
        ["kind"] = "state_move",
        ["affected"] = affected,
        ["blocksApply"] = false,
        ["workflow"] = "task_flow",
        ["from"] = from,
        ["to"] = to,
    };
}
