using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Cli;
using PmTool.Blueprint;
using PmTool.Cli.Documents;
using PmTool.Identity;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// CLI 的藍圖主線：匯出、改檔、plan、apply 來回一次，結果正確。
/// 權杖是真的、API 是真的、資料庫是真的；CLI 只換掉網路與終端機。
/// </summary>
[TestFixture]
public sealed class CliBlueprintFlowTests : RealStackTestBase
{
    [Test]
    public async Task ExportEditPlanApply_AddsTheStateAndTransitionsAndRaisesTheVersionByOne()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "AGY", "agency_project");
        using var cli = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);
        var file = cli.PathOf("agy.yaml");

        // Act
        var export = await cli.RunAsync("blueprint", "export", "--project", "AGY", "-o", file);
        var edited = EditAddingLegalReview(await File.ReadAllTextAsync(file));
        await File.WriteAllTextAsync(file, edited);
        var plan = await cli.RunAsync("blueprint", "plan", "-f", file);
        var listedAfterPlan = await RealStackSteps.GetAsync(owner.Client, ChangeSetSteps.RouteOf("AGY"));
        var apply = await cli.RunAsync("blueprint", "apply", "-f", file, "--summary", "加上法務審查");
        var project = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/AGY");
        var changeSets = await RealStackSteps.GetAsync(owner.Client, ChangeSetSteps.RouteOf("AGY"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(export.ExitCode, Is.EqualTo(ExitCodes.Success), export.Error);
            Assert.That(export.Out, Does.Contain("第 1 版"));
            Assert.That(edited, Does.StartWith("# project: AGY\nbaseVersion: 1\n"));
            Assert.That(plan.ExitCode, Is.EqualTo(ExitCodes.Success), plan.Error);
            Assert.That(plan.Out, Does.Contain("+ 新增 狀態 legal_review").And.Contain("+ 新增 轉換"));
            Assert.That(listedAfterPlan.AsArray(), Is.Empty, "plan 什麼都不存");
            Assert.That(apply.ExitCode, Is.EqualTo(ExitCodes.Success), apply.Error);
            Assert.That(apply.Out, Does.Contain("已套用 #1").And.Contain("第 2 版"));
            Assert.That(project["project"]!["blueprintVersion"]!.GetValue<long>(), Is.EqualTo(2));
            Assert.That(ChangeSetSteps.StateKeysOf(project["blueprint"], "delivery"), Does.Contain("legal_review"));
            Assert.That(
                ChangeSetSteps.DescribeTransitions(project["blueprint"], "delivery"),
                Does.Contain("quoting->legal_review|").And.Contain("legal_review->designing|"));
            Assert.That(changeSets.AsArray().Single()!["summary"]!.GetValue<string>(), Is.EqualTo("加上法務審查"));
        }
    }

    [Test]
    public async Task Plan_AfterTheProjectMovedOn_IsBlockedWithTheVersionMismatchAndExitsWithThree()
    {
        // Arrange：同一份檔案先套用一次，專案升到第 2 版；再拿它（基準還是第 1 版）預覽。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "STALE", "simple_tasks");
        using var cli = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);
        var file = cli.PathOf("stale.yaml");
        await cli.RunAsync("blueprint", "export", "-p", "STALE", "-o", file);
        var edited = EditRoot(await File.ReadAllTextAsync(file), root =>
            root["blueprint"] = ChangeSetSteps.WithState(root["blueprint"]!.AsObject(), "task_flow", "review", "審查中", "in_progress"));
        await File.WriteAllTextAsync(file, edited);
        await cli.RunAsync("blueprint", "apply", "-f", file);

        // Act
        var plan = await cli.RunAsync("blueprint", "plan", "-f", file);

        // Assert
        Assert.That(plan.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision), plan.Error);
        Assert.That(plan.Out, Does.Contain("以第 1 版為基準，專案目前是第 2 版").And.Contain("現在還不能套用"));
    }

    [Test]
    public async Task Apply_RemovingAStateWithItems_ListsTheImpactAndNeedsYesBeforeMovingTheItems()
    {
        // Arrange：兩個項目在「進行中」，檔案拿掉這個狀態並指定搬到「待辦」。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "MOVE", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "MOVE", new { type = "task", title = "第一個" });
        await RealStackSteps.CreateItemAsync(owner.Client, "MOVE", new { type = "task", title = "第二個" });
        await RealStackSteps.TransitionThroughAsync(owner.Client, "MOVE-1", "doing");
        await RealStackSteps.TransitionThroughAsync(owner.Client, "MOVE-2", "doing");
        using var cli = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);
        var file = cli.PathOf("move.yaml");
        await cli.RunAsync("blueprint", "export", "-p", "MOVE", "-o", file);
        await File.WriteAllTextAsync(file, EditRoot(await File.ReadAllTextAsync(file), root =>
        {
            root["blueprint"] = ChangeSetSteps.WithoutState(root["blueprint"]!.AsObject(), "task_flow", "doing");
            root["migrations"] = new JsonObject
            {
                ["moveStates"] = new JsonArray(ChangeSetSteps.MoveState("task_flow", "doing", "todo")),
            };
        }));

        // Act
        var plan = await cli.RunAsync("blueprint", "plan", "-f", file);
        var withoutYes = await cli.RunAsync("blueprint", "apply", "-f", file);
        var changeSetsAfterRefusal = await RealStackSteps.GetAsync(owner.Client, ChangeSetSteps.RouteOf("MOVE"));
        var withYes = await cli.RunAsync("blueprint", "apply", "-f", file, "--yes");
        var moved = await RealStackSteps.GetAsync(owner.Client, "/v1/items/MOVE-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision), plan.Error);
            Assert.That(plan.Out, Does.Contain("2 個項目會搬到 todo"));
            Assert.That(withoutYes.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision));
            Assert.That(changeSetsAfterRefusal.AsArray(), Is.Empty, "沒有 --yes 就沒有送出");
            Assert.That(withYes.ExitCode, Is.EqualTo(ExitCodes.Success), withYes.Error);
            Assert.That(withYes.Out, Does.Contain("已套用 #1"));
            Assert.That(moved["state"]!.GetValue<string>(), Is.EqualTo("todo"));
        }
    }

    [Test]
    public async Task Apply_TokenWithoutBlueprintWriteScope_ExitsWithOneAndNamesTheScope_WhilePlanStillWorks()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "SCOPE", "simple_tasks");
        using var admin = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);
        using var readOnly = await CliSession.CreateAsync(Factory, owner, AccessTokenScope.BlueprintRead, AccessTokenScope.ItemsRead);
        var file = admin.PathOf("scope.yaml");
        await admin.RunAsync("blueprint", "export", "-p", "SCOPE", "-o", file);
        await File.WriteAllTextAsync(file, EditRoot(await File.ReadAllTextAsync(file), root =>
            root["blueprint"] = ChangeSetSteps.WithState(root["blueprint"]!.AsObject(), "task_flow", "review", "審查中", "in_progress")));

        // Act
        var plan = await readOnly.RunAsync("blueprint", "plan", "-f", file);
        var apply = await readOnly.RunAsync("blueprint", "apply", "-f", file);
        var changeSets = await RealStackSteps.GetAsync(owner.Client, ChangeSetSteps.RouteOf("SCOPE"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.ExitCode, Is.EqualTo(ExitCodes.Success), plan.Error);
            Assert.That(apply.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(apply.Error, Does.Contain("blueprint:write"));
            Assert.That(changeSets.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task ChangeSetCommands_ListShowAndReject_WorkAgainstARealProposal()
    {
        // Arrange：用 API 送一份等待核准的變更集，再用 CLI 看、拒絕。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "CSET", "simple_tasks");
        var blueprint = await ChangeSetSteps.BlueprintOfAsync(owner.Client, "CSET");
        await ChangeSetSteps.ProposedAsync(owner.Client, "CSET", ChangeSetSteps.Json(
            1, "加上審查中", ChangeSetSteps.WithState(blueprint, "task_flow", "review", "審查中", "in_progress")));
        using var cli = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);

        // Act
        var list = await cli.RunAsync("changeset", "list", "-p", "CSET");
        var show = await cli.RunAsync("changeset", "show", "1", "-p", "CSET");
        var reject = await cli.RunAsync("changeset", "reject", "1", "-p", "CSET");
        var rejectAgain = await cli.RunAsync("changeset", "reject", "1", "-p", "CSET");
        var apply = await cli.RunAsync("changeset", "apply", "1", "-p", "CSET");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(list.Out, Does.Contain("#1").And.Contain("等待核准").And.Contain("加上審查中"));
            Assert.That(show.Out, Does.Contain("+ 新增 狀態 review"));
            Assert.That(reject.ExitCode, Is.EqualTo(ExitCodes.Success), reject.Error);
            Assert.That(rejectAgain.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(apply.ExitCode, Is.EqualTo(ExitCodes.Error));
        }
    }

    [Test]
    public async Task Validate_TheExportedFile_PassesLocallyEvenWithoutAnyCredentials()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "LOCAL", "agency_project");
        using var cli = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);
        var file = cli.PathOf("local.yaml");
        await cli.RunAsync("blueprint", "export", "-p", "LOCAL", "-o", file);

        // Act
        var result = await cli.RunWithConfigFileAsync(string.Empty, "blueprint", "validate", "-f", file);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
    }

    /// <summary>在「報價」之後加一個法務審查：狀態加兩條轉換。像使用者用編輯器改檔那樣，改完寫回 YAML。</summary>
    private static string EditAddingLegalReview(string yaml) => EditRoot(yaml, root =>
    {
        var blueprint = ChangeSetSteps.WithState(root["blueprint"]!.AsObject(), "delivery", "legal_review", "法務審查", "in_progress");
        var delivery = blueprint["workflows"]!.AsArray().Single(w => w!["key"]!.GetValue<string>() == "delivery")!;
        var transitions = delivery["transitions"]!.AsArray();
        transitions.Add(ChangeSetSteps.Transition("quoting", "legal_review"));
        transitions.Add(ChangeSetSteps.Transition("legal_review", "designing"));
        root["blueprint"] = blueprint;
    });

    private static string EditRoot(string yaml, Action<JsonObject> edit)
    {
        var root = new DocumentText(yaml, DocumentFormat.Yaml, "匯出").ToJson()!.AsObject();
        edit(root);
        var project = new DocumentText(yaml, DocumentFormat.Yaml, "匯出").ProjectKeyFromComment()!;
        var rendered = BlueprintExport.Render(project, (long)root["baseVersion"]!, JsonSerializer.SerializeToElement(root["blueprint"]), DocumentFormat.Yaml);
        return root["migrations"] is { } migrations
            ? rendered + "migrations: " + migrations.ToJsonString() + "\n"
            : rendered;
    }
}
