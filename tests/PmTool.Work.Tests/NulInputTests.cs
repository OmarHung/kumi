using PmTool.Abstractions;

namespace PmTool.Work.Tests;

/// <summary>
/// 每一個會進資料庫的字串輸入都帶 NUL。PostgreSQL 的 text 與 jsonb 存不下 NUL，連當成查詢參數都會出錯；
/// 這些值要在模組裡就被規則擋下來（或確定找不到），不能變成例外。之後 CLI 與 MCP 也直接呼叫這些介面。
/// </summary>
[TestFixture]
public sealed class NulInputTests
{
    private static readonly string Nul = ((char)0x0000).ToString();

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    public static IEnumerable<TestCaseData> Inputs()
    {
        yield return Case("開專案的代號", FailureKind.Invalid, WorkErrorCodes.ProjectKeyInvalid, (h, s) =>
            FailureOf(h.Projects.CreateAsync(s.Admin, new NewProject("NEW" + Nul, "名稱", Scenario.SimpleTasks), None)));
        yield return Case("開專案的名稱", FailureKind.Invalid, WorkErrorCodes.ProjectNameRequired, (h, s) =>
            FailureOf(h.Projects.CreateAsync(s.Admin, new NewProject("NEW", "名稱" + Nul, Scenario.SimpleTasks), None)));
        yield return Case("開專案的模板", FailureKind.Invalid, WorkErrorCodes.TemplateNotFound, (h, s) =>
            FailureOf(h.Projects.CreateAsync(s.Admin, new NewProject("NEW", "名稱", Scenario.SimpleTasks + Nul), None)));
        yield return Case("讀專案的代號", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, (h, s) =>
            FailureOf(h.Projects.GetAsync(s.Admin, "WEB" + Nul, None)));
        yield return Case("成員的角色", FailureKind.Invalid, WorkErrorCodes.MemberUnknownRole, (h, s) =>
            FailureOf(h.Projects.SetMemberAsync(s.Admin, "WEB", Guid.NewGuid(), "viewer" + Nul, None)));
        yield return Case("新增項目的類型", FailureKind.Invalid, WorkErrorCodes.ItemUnknownType, (h, s) =>
            FailureOf(h.Items.CreateAsync(s.Admin, "WEB", new NewWorkItem { Type = "task" + Nul, Title = "任務" }, None)));
        yield return Case("修改項目的標題", FailureKind.Invalid, WorkErrorCodes.ItemInvalidValue, (h, s) =>
            Patch(h, s, new WorkItemPatch { Title = "標題" + Nul }));
        yield return Case("自訂欄位的 key", FailureKind.Invalid, WorkErrorCodes.ItemUnknownField, (h, s) =>
            Patch(h, s, new WorkItemPatch { Fields = Scenario.Fields(("summary" + Nul, "說明")) }));
        foreach (var (field, value) in FieldValues())
        {
            yield return Case($"欄位 {field} 的值", FailureKind.Invalid, WorkErrorCodes.ItemInvalidValue, (h, s) =>
                Patch(h, s, new WorkItemPatch { Fields = Scenario.Fields((field, value)) }));
        }

        yield return Case("上層項目", FailureKind.Invalid, WorkErrorCodes.ItemUnknownParent, (h, s) =>
            Patch(h, s, new WorkItemPatch { ParentKey = "WEB-1" + Nul }));
        yield return Case("新增項目的說明", FailureKind.Invalid, WorkErrorCodes.ItemInvalidValue, (h, s) =>
            FailureOf(h.Items.CreateAsync(s.Admin, "WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "說明" + Nul }, None)));
        yield return Case("修改項目的說明", FailureKind.Invalid, WorkErrorCodes.ItemInvalidValue, (h, s) =>
            Patch(h, s, new WorkItemPatch { Description = Nul + "說明" }));
        yield return Case("項目編號的流水號", FailureKind.NotFound, WorkErrorCodes.ItemNotFound, (h, s) =>
            FailureOf(h.Items.GetAsync(s.Admin, "WEB-1" + Nul, None)));
        yield return Case("項目編號的專案代號", FailureKind.NotFound, WorkErrorCodes.ItemNotFound, (h, s) =>
            FailureOf(h.Items.GetAsync(s.Admin, "WEB" + Nul + "-1", None)));
        yield return Case("要轉去的狀態", FailureKind.Invalid, WorkErrorCodes.TransitionNotAllowed, (h, s) =>
            FailureOf(h.Items.TransitionAsync(s.Admin, "WEB-1", "doing" + Nul, None)));
        yield return Case("改類型的類型", FailureKind.Invalid, WorkErrorCodes.ItemUnknownType, (h, s) =>
            FailureOf(h.Items.ChangeTypeAsync(s.Admin, "WEB-1", "task" + Nul, None)));
        yield return Case("改類型的項目編號", FailureKind.NotFound, WorkErrorCodes.ItemNotFound, (h, s) =>
            FailureOf(h.Items.ChangeTypeAsync(s.Admin, "WEB-1" + Nul, "task", None)));
        yield return Case("刪除的項目編號", FailureKind.NotFound, WorkErrorCodes.ItemNotFound, (h, s) =>
            FailureOf(h.Items.DeleteAsync(s.Admin, "WEB" + Nul + "-1", None)));
        yield return Case("移除成員的專案代號", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, (h, s) =>
            FailureOf(h.Projects.RemoveMemberAsync(s.Admin, "WEB" + Nul, s.Admin.PersonId, None)));
        yield return Case("清單的專案代號", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, (h, s) =>
            FailureOf(h.Items.ListAsync(s.Admin, "WEB" + Nul, new WorkItemQuery(), None)));
        yield return Case("篩選的類型", FailureKind.Invalid, WorkErrorCodes.QueryInvalid, (h, s) => List(h, s, new WorkItemQuery { Types = [Nul] }));
        yield return Case("篩選的狀態", FailureKind.Invalid, WorkErrorCodes.QueryInvalid, (h, s) => List(h, s, new WorkItemQuery { States = ["a" + Nul + "b"] }));
        yield return Case("搜尋的文字", FailureKind.Invalid, WorkErrorCodes.QueryInvalid, (h, s) => List(h, s, new WorkItemQuery { Search = "登入" + Nul }));
        yield return Case("分頁的游標", FailureKind.Invalid, WorkErrorCodes.QueryInvalid, (h, s) => List(h, s, new WorkItemQuery { Cursor = Nul }));
        yield return Case("篩選的上層項目", FailureKind.Invalid, WorkErrorCodes.QueryInvalid, (h, s) => List(h, s, new WorkItemQuery { ParentKey = "WEB-1" + Nul }));
        yield return Case("活動的項目編號", FailureKind.NotFound, WorkErrorCodes.ItemNotFound, (h, s) =>
            FailureOf(h.Activity.ListAsync(s.Admin, "WEB-1" + Nul, new ActivityQuery(), None)));
        yield return Case("活動的分頁游標", FailureKind.Invalid, WorkErrorCodes.QueryInvalid, (h, s) =>
            FailureOf(h.Activity.ListAsync(s.Admin, "WEB-1", new ActivityQuery { Cursor = "YTo" + Nul }, None)));
        yield return Case("留言的項目編號", FailureKind.NotFound, WorkErrorCodes.ItemNotFound, (h, s) =>
            FailureOf(h.Activity.AddCommentAsync(s.Admin, "WEB" + Nul + "-1", "留言", null, None)));
        yield return Case("留言的內容", FailureKind.Invalid, WorkErrorCodes.CommentBodyInvalid, (h, s) =>
            FailureOf(h.Activity.AddCommentAsync(s.Admin, "WEB-1", "留言" + Nul, null, None)));
        yield return Case("變更集的摘要", FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid, ProposeWithNulSummaryAsync);
        yield return Case("送出變更集的專案代號", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, (h, s) =>
            FailureOf(h.Changes.ProposeAsync(s.Admin, "WEB" + Nul, "{}", Blueprint.DocumentFormat.Json, None)));
        yield return Case("變更集清單的專案代號", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, (h, s) =>
            FailureOf(h.Changes.ListAsync(s.Admin, "WEB" + Nul, None)));
        yield return Case("讀變更集的專案代號", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, (h, s) =>
            FailureOf(h.Changes.GetAsync(s.Admin, "WEB" + Nul, 1, None)));
        yield return Case("套用變更集的專案代號", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, (h, s) =>
            FailureOf(h.Changes.ApplyAsync(s.Admin, "WEB" + Nul, 1, None)));
        yield return Case("拒絕變更集的專案代號", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, (h, s) =>
            FailureOf(h.Changes.RejectAsync(s.Admin, "WEB" + Nul, 1, None)));
    }

    [TestCaseSource(nameof(Inputs))]
    public async Task EveryStringInput_WithNul_IsRejectedByTheRulesInsteadOfThrowing(
        FailureKind kind, string code, Operation operation)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB");

        // Act
        var failure = await operation.Run(_host, scenario);

        // Assert
        Assert.That(failure, Is.Not.Null, "應該被擋下來");
        Assert.That((failure!.Kind, failure.Code), Is.EqualTo((kind, code)));
    }

    /// <summary>simple_tasks 每種字串型別的欄位各一個，值的中間或尾端帶 NUL。</summary>
    private static IEnumerable<(string Field, object Value)> FieldValues()
    {
        yield return ("summary", "說明" + Nul);
        yield return ("reference_url", "https://example.com/" + Nul);
        yield return ("category", "bug" + Nul);
        yield return ("tags", new[] { "docs" + Nul });
        yield return ("review_date", "2026-11-30" + Nul);
        yield return ("reviewer", Guid.Empty.ToString() + Nul);
    }

    private static CancellationToken None => CancellationToken.None;

    private static async Task<Failure?> ProposeWithNulSummaryAsync(WorkTestHost host, Scenario scenario)
    {
        var (blueprint, version) = await ChangeSetDrafts.CurrentAsync(scenario, "WEB");
        var document = ChangeSetDrafts.Json(version, blueprint, summary: "摘要" + Nul);
        return await FailureOf(host.Changes.ProposeAsync(scenario.Admin, "WEB", document, Blueprint.DocumentFormat.Json, None));
    }

    private static async Task<Failure?> FailureOf<T>(Task<Result<T>> operation) => (await operation).Failure;

    private static Task<Failure?> Patch(WorkTestHost host, Scenario scenario, WorkItemPatch patch) =>
        FailureOf(host.Items.UpdateAsync(scenario.Admin, "WEB-1", patch, None));

    private static Task<Failure?> List(WorkTestHost host, Scenario scenario, WorkItemQuery query) =>
        FailureOf(host.Items.ListAsync(scenario.Admin, "WEB", query, None));

    private static TestCaseData Case(
        string input, FailureKind kind, string code, Func<WorkTestHost, Scenario, Task<Failure?>> act) =>
        new TestCaseData(kind, code, new Operation(act)).SetArgDisplayNames(input);

    /// <summary>包一層：測試方法是 public，參數不能直接用 internal 的 WorkTestHost 與 Scenario。</summary>
    public sealed class Operation
    {
        internal Operation(Func<WorkTestHost, Scenario, Task<Failure?>> run) => Run = run;

        internal Func<WorkTestHost, Scenario, Task<Failure?>> Run { get; }
    }
}
