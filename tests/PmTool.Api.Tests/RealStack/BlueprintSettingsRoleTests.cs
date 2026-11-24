using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 網頁設定頁第二片會送出的變更集（自訂角色）：新增角色、勾權限、用在轉換的允許角色上；刪除還有成員的角色會被擋，
/// 成員換掉角色之後重新讀預覽就可以套用。形狀照網頁組的，見 <see cref="ChangeSetSteps.SettingsJson"/>。
/// 這個類別共用一個應用程式實體，登入每分鐘限 10 次：第一個測試登入三次，第二個只有擁有者登入。
/// </summary>
[TestFixture]
public sealed class BlueprintSettingsRoleTests : RealStackTestBase
{
    // 依合約列出權限的順序，和網頁勾選後送出的一樣。
    private static readonly string[] QaLeadPermissions =
    [
        WorkPermissions.ItemRead, WorkPermissions.ItemUpdate, WorkPermissions.ItemTransition,
        WorkPermissions.CommentCreate, WorkPermissions.BlueprintRead,
    ];

    [Test]
    public async Task Apply_CustomRoleOnATransition_LetsItsMembersTakeTheTransitionButNotEditors()
    {
        // Arrange：一個需求停在開發；套用之後加一位 QA 主管、一位編輯者。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "QALEAD", "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, "QALEAD", new { type = "requirement", title = "首頁改版需求" });
        await RealStackSteps.TransitionThroughAsync(owner.Client, "QALEAD-1", "quoting", "designing", "developing");
        var proposed = await ChangeSetSteps.ProposedAsync(owner.Client, "QALEAD", ChangeSetSteps.SettingsJson(
            1, "內部驗收由 QA 主管放行", WithQaLeadOnInternalQa(await ChangeSetSteps.BlueprintOfAsync(owner.Client, "QALEAD"))));
        await ChangeSetSteps.AppliedAsync(owner.Client, "QALEAD", 1);
        using var qaLead = await AddMemberAsync(owner, "QALEAD", "qa_lead", "QA Lead Example", "qa-lead@example.com");
        using var editor = await AddMemberAsync(owner, "QALEAD", "editor", "Editor Example", "editor@example.com");

        // Act
        using var asEditor = await RealStackSteps.TransitionAsync(editor.Client, "QALEAD-1", "internal_qa");
        var asQaLead = await RealStackSteps.TransitionThroughAsync(qaLead.Client, "QALEAD-1", "internal_qa");
        var detail = await RealStackSteps.GetAsync(qaLead.Client, "/v1/projects/QALEAD");

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(asEditor, HttpStatusCode.Forbidden, WorkErrorCodes.TransitionRoleRequired);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ChangeSetSteps.DescribeChanges(proposed["changes"]), Is.EqualTo(new[]
            {
                "modified|transition|developing->internal_qa|delivery|allowedRoles",
                "added|role|qa_lead||",
            }));
            Assert.That(proposed["impacts"]?.AsArray(), Is.Empty);
            Assert.That(proposed["canApply"]?.GetValue<bool>(), Is.True);
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.TransitionRoleRequired}|role" }));
            Assert.That(details[0]?["message"]?.GetValue<string>(), Does.Contain("「QA 主管」").And.Contain("qa_lead"));
            Assert.That(asQaLead["stateCategory"]?.GetValue<string>(), Is.EqualTo("in_progress"));
            Assert.That(detail["project"]?["myRole"]?.GetValue<string>(), Is.EqualTo("qa_lead"));
            Assert.That(detail["myPermissions"]?.AsArray().Select(p => p?.GetValue<string>()), Is.EqualTo(QaLeadPermissions));
        }
    }

    [Test]
    public async Task Apply_RemovingARoleMembersStillHave_IsBlockedUntilEveryoneIsReassigned()
    {
        // Arrange：QA 主管已經套用（藍圖第 2 版），兩位成員是 QA 主管。他們不必登入，省下登入的額度。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "NOQA", "agency_project");
        await ChangeSetSteps.ProposedAsync(owner.Client, "NOQA", ChangeSetSteps.SettingsJson(
            1, "內部驗收由 QA 主管放行", WithQaLeadOnInternalQa(await ChangeSetSteps.BlueprintOfAsync(owner.Client, "NOQA"))));
        await ChangeSetSteps.AppliedAsync(owner.Client, "NOQA", 1);
        Guid[] qaLeads =
        [
            await AddQaLeadAsync(owner, "NOQA", "QA One Example", "qa-one@example.com"),
            await AddQaLeadAsync(owner, "NOQA", "QA Two Example", "qa-two@example.com"),
        ];
        var withoutQaLead = ChangeSetSteps.WithoutRole(await ChangeSetSteps.BlueprintOfAsync(owner.Client, "NOQA"), "qa_lead");

        // Act
        var proposed = await ChangeSetSteps.ProposedAsync(owner.Client, "NOQA", ChangeSetSteps.SettingsJson(2, "不再需要 QA 主管", withoutQaLead));
        using var blocked = await ChangeSetSteps.ApplyAsync(owner.Client, "NOQA", 2);
        foreach (var person in qaLeads)
        {
            await RealStackSteps.SetMemberAsync(owner.Client, "NOQA", person, "editor");
        }

        var reassigned = await ChangeSetSteps.GetAsync(owner.Client, "NOQA", 2);
        var applied = await ChangeSetSteps.AppliedAsync(owner.Client, "NOQA", 2);
        var project = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/NOQA");

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(blocked, HttpStatusCode.Conflict, WorkErrorCodes.ChangeSetApplyBlocked);
        string[] blocker = [$"{WorkErrorCodes.ChangeSetMembersWithRemovedRole}|impacts/0"];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ChangeSetSteps.DescribeChanges(proposed["changes"]), Is.EqualTo(new[]
            {
                "modified|transition|developing->internal_qa|delivery|allowedRoles",
                "removed|role|qa_lead||",
            }));
            Assert.That(JsonNode.DeepEquals(proposed["impacts"], RoleRemoval(affected: 2, blocksApply: true)), Is.True, proposed["impacts"]?.ToJsonString());
            Assert.That(ApiRequests.Describe(proposed["blockers"]!.AsArray()), Is.EqualTo(blocker));
            Assert.That(proposed["blockers"]?[0]?["message"]?.GetValue<string>(), Does.Contain("「QA 主管」").And.Contain("2 位成員"));
            Assert.That(proposed["canApply"]?.GetValue<bool>(), Is.False);
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(blocker));
            Assert.That(ChangeSetSteps.StatusOf(reassigned), Is.EqualTo("pending"), "被擋下的套用沒有動到變更集");
            Assert.That(JsonNode.DeepEquals(reassigned["impacts"], RoleRemoval(affected: 0, blocksApply: false)), Is.True, reassigned["impacts"]?.ToJsonString());
            Assert.That(reassigned["blockers"]?.AsArray(), Is.Empty);
            Assert.That(reassigned["canApply"]?.GetValue<bool>(), Is.True);
            Assert.That(ChangeSetSteps.Describe(applied["changeSet"]), Is.EqualTo("#2 applied base=2 applied=3 by=Owner Example decidedBy=Owner Example"));
            Assert.That(RealStackSteps.KeysOf(project["blueprint"]?["roles"]), Is.EqualTo(new[] { "project_manager", "client" }));
            Assert.That(ChangeSetSteps.DescribeTransitions(project["blueprint"], "delivery"), Does.Contain("developing->internal_qa|"));
        }
    }

    /// <summary>加上 QA 主管，開發到內部驗收只有 QA 主管可以送；其餘的轉換原樣帶回。</summary>
    private static JsonObject WithQaLeadOnInternalQa(JsonObject blueprint) =>
        ChangeSetSteps.WithTransitionReplaced(
            ChangeSetSteps.WithRole(blueprint, "qa_lead", "QA 主管", QaLeadPermissions),
            "delivery",
            "developing",
            "internal_qa",
            ChangeSetSteps.Transition("developing", "internal_qa", "qa_lead"));

    /// <summary>新增組織成員、在專案裡指派成 QA 主管；回傳人員的識別碼。</summary>
    private static async Task<Guid> AddQaLeadAsync(SignedInClient owner, string projectKey, string displayName, string email)
    {
        var person = await RealStackSteps.CreateMemberAsync(owner.Client, displayName, email);
        var personId = person["personId"]!.GetValue<Guid>();
        await RealStackSteps.SetMemberAsync(owner.Client, projectKey, personId, "qa_lead");
        return personId;
    }

    private static JsonArray RoleRemoval(long affected, bool blocksApply) => new(new JsonObject
    {
        ["kind"] = "role_removal",
        ["affected"] = affected,
        ["blocksApply"] = blocksApply,
        ["role"] = "qa_lead",
    });
}
