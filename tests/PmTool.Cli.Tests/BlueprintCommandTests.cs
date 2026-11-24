using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint;
using PmTool.Cli.Documents;

namespace PmTool.Cli.Tests;

/// <summary>kumi blueprint 的四個指令與 apply 的流程。</summary>
[TestFixture]
public sealed class BlueprintCommandTests
{
    private const string Preview = "/v1/projects/WEB/changesets/preview";
    private const string Propose = "/v1/projects/WEB/changesets";

    private CliHarness _cli = null!;

    [SetUp]
    public void CreateHarness() => _cli = new CliHarness().LoggedInByEnvironment();

    [TearDown]
    public void DisposeHarness() => _cli.Dispose();

    private static object Plan(
        object[]? changes = null, object[]? impacts = null, object[]? blockers = null, bool canApply = true, int baseVersion = 1, int currentVersion = 1) =>
        new
        {
            baseVersion,
            currentVersion,
            blueprint = new { },
            changes = changes ?? [new { kind = "added", resource = "state", key = "review", parentKey = "main", changedProperties = Array.Empty<string>() }],
            impacts = impacts ?? [],
            blockers = blockers ?? [],
            canApply,
        };

    private static object StateMove(long affected, bool blocks = false) =>
        new { kind = "state_move", affected, blocksApply = blocks, workflow = "main", from = "review", to = "done" };

    private string WriteFile(string name, string text)
    {
        var path = _cli.PathOf(name);
        File.WriteAllText(path, text);
        return path;
    }

    private static object Detail(long number, string status = "pending", long? appliedVersion = null) =>
        new { changeSet = new { number, status, appliedVersion }, changes = Array.Empty<object>(), impacts = Array.Empty<object>(), blockers = Array.Empty<object>(), canApply = true };

    // ---- plan ----

    [Test]
    public async Task Plan_ShowsTheDiffAndExitsWithZeroWhenNothingNeedsADecision()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan());
        var path = WriteFile("plan.yaml", "# project: WEB\nbaseVersion: 1\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "plan", "-f", path);

        // Assert
        var request = _cli.Server.Requests.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(request.ContentType, Is.EqualTo("application/yaml"));
            Assert.That(request.Body, Is.EqualTo("# project: WEB\nbaseVersion: 1\n"), "照原文送，行號才對得上");
            Assert.That(result.Out, Does.Contain("專案 WEB").And.Contain("差異（1）").And.Contain("+ 新增 狀態 review（main 底下）"));
        }
    }

    [Test]
    public async Task Plan_JsonFile_IsSentAsJsonAndNeedsTheProjectOption()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan());
        var path = WriteFile("plan.json", "{\"baseVersion\":1,\"blueprint\":{}}");

        // Act
        var without = await _cli.RunAsync("blueprint", "plan", "-f", path);
        var with = await _cli.RunAsync("blueprint", "plan", "-f", path, "--project", "WEB");

        // Assert
        Assert.That(without.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(without.Error, Does.Contain("--project"));
        Assert.That(with.ExitCode, Is.EqualTo(ExitCodes.Success), with.Error);
        Assert.That(_cli.Server.Requests.Single().ContentType, Is.EqualTo("application/json"));
    }

    [Test]
    public async Task Plan_ImpactsOnExistingData_ExitWithThreeAndDescribeEveryKind()
    {
        // Arrange
        object[] impacts =
        [
            StateMove(12),
            new { kind = "field_drop", affected = 4L, blocksApply = false, field = "legacy" },
            new { kind = "field_conversion", affected = 7L, blocksApply = false, field = "budget", fromType = "text", toType = "number", onFailure = "leave_empty" },
            new { kind = "item_type_removal", affected = 3L, blocksApply = true, itemType = "bug" },
            new { kind = "parent_removal", affected = 1L, blocksApply = true, itemType = "task", parent = "epic" },
            new { kind = "role_removal", affected = 2L, blocksApply = true, role = "client" },
            new { kind = "field_detach", affected = 5L, blocksApply = false, itemType = "task", field = "summary" },
            new { kind = "future_kind", affected = 1L, blocksApply = false },
        ];
        _cli.Server.On("POST", Preview, Plan(impacts: impacts));
        var path = WriteFile("plan.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "plan", "-f", path);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision));
            Assert.That(result.Out, Does.Contain("對既有資料的影響"));
            Assert.That(result.Out, Does.Contain("12 個項目會搬到 done").And.Contain("欄位 legacy 被刪除，4 個項目").And.Contain("從 text 轉成 number"));
            Assert.That(result.Out, Does.Contain("類型 bug 被刪除，底下還有 3 個項目（擋住套用）").And.Contain("task 不能再放在 epic 底下"));
            Assert.That(result.Out, Does.Contain("角色 client 被刪除").And.Contain("不再有欄位 summary").And.Contain("future_kind：1 個項目"));
        }
    }

    [Test]
    public async Task Plan_Blockers_AreListedAndExitWithThree()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(
            blockers: [new { code = "plan.base_version_mismatch", path = "/baseVersion", message = "請依目前的版本重新產生。" }], canApply: false, baseVersion: 1, currentVersion: 2));
        var path = WriteFile("plan.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "plan", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision));
        Assert.That(result.Out, Does.Contain("以第 1 版為基準，專案目前是第 2 版").And.Contain("現在還不能套用").And.Contain("請依目前的版本重新產生。"));
    }

    [Test]
    public async Task Plan_NoChanges_SaysSoAndExitsWithZero()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(changes: []));
        var path = WriteFile("plan.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "plan", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(result.Out, Does.Contain("沒有差異"));
    }

    [Test]
    public async Task Plan_Json_PrintsThePreviewDataAsIs()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(impacts: [StateMove(2)]));
        var path = WriteFile("plan.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "plan", "-f", path, "--json");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision));
        Assert.That(JsonDocument.Parse(result.Out).RootElement.GetProperty("impacts")[0].GetProperty("affected").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public async Task Plan_ServerSaysTheDocumentIsInvalid_ExitsWithTwoAndShowsEachProblem()
    {
        // Arrange
        _cli.Server.OnError("POST", Preview, HttpStatusCode.UnprocessableEntity, "changeset.invalid", "變更集沒有通過檢查：缺少 blueprint。", ("/blueprint", "缺少 blueprint。"));
        var path = WriteFile("plan.yaml", "# project: WEB\nbaseVersion: 1\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "plan", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("/blueprint：缺少 blueprint。"));
    }

    [Test]
    public async Task Plan_ProjectOptionAndHeaderDisagree_IsAUsageErrorAndAgreeingOnesAreFine()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan());
        _cli.Server.On("POST", "/v1/projects/web/changesets/preview", Plan());
        var path = WriteFile("plan.yaml", "# project: WEB\n");

        // Act
        var disagree = await _cli.RunAsync("blueprint", "plan", "-f", path, "-p", "OTHER");
        var agree = await _cli.RunAsync("blueprint", "plan", "-f", path, "-p", "web");

        // Assert
        Assert.That(disagree.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(disagree.Error, Does.Contain("WEB").And.Contain("OTHER"));
        Assert.That(agree.ExitCode, Is.EqualTo(ExitCodes.Success), agree.Error);
    }

    [Test]
    public async Task Plan_NoProjectAnywhere_AsksForOne()
    {
        // Arrange
        var path = WriteFile("plan.yaml", "baseVersion: 1\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "plan", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--project").And.Contain("# project:"));
        Assert.That(_cli.Server.Requests, Is.Empty);
    }

    // ---- apply ----

    [Test]
    public async Task Apply_PermittedAndNoDataImpact_SubmitsThenAppliesAndReportsTheNewVersion()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(canApply: true));
        _cli.Server.On("POST", Propose, Detail(5), HttpStatusCode.Created);
        _cli.Server.On("POST", $"{Propose}/5/apply", Detail(5, "applied", appliedVersion: 4));
        var path = WriteFile("apply.yaml", "# project: WEB\nbaseVersion: 3\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_cli.Server.Requests.Select(r => r.Path), Is.EqualTo(new[] { Preview, Propose, $"{Propose}/5/apply" }));
            Assert.That(_cli.Server.To("POST", Propose).Single().Body, Is.EqualTo("# project: WEB\nbaseVersion: 3\n"));
            Assert.That(result.Out, Does.Contain("差異（1）").And.Contain("已套用 #5").And.Contain("第 4 版"));
        }
    }

    [Test]
    public async Task Apply_NotPermittedToApply_SubmitsAndSaysItWaitsForApproval()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(canApply: false));
        _cli.Server.On("POST", Propose, Detail(6), HttpStatusCode.Created);
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("已送出 #6").And.Contain("等待"));
        Assert.That(_cli.Server.Requests.Select(r => r.Path), Is.EqualTo(new[] { Preview, Propose }));
    }

    [Test]
    public async Task Apply_Blockers_ListThemAndExitWithThreeWithoutSubmitting()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(
            impacts: [new { kind = "item_type_removal", affected = 3L, blocksApply = true, itemType = "bug" }],
            blockers: [new { code = "changeset.items_of_removed_type", path = "impacts/0", message = "「錯誤」底下還有 3 個項目，要先處理才能刪除這個類型。" }],
            canApply: false));
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path, "--yes");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision));
        Assert.That(result.Out, Does.Contain("底下還有 3 個項目").And.Contain("沒有送出"));
        Assert.That(_cli.Server.Requests.Select(r => r.Path), Is.EqualTo(new[] { Preview }));
    }

    [Test]
    public async Task Apply_DataImpactWithoutYesAndNotInteractive_ListsItAndExitsWithThreeWithoutSubmitting()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(impacts: [StateMove(12)]));
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision));
        Assert.That(result.Out, Does.Contain("12 個項目會搬到 done").And.Contain("--yes"));
        Assert.That(_cli.Server.Requests.Select(r => r.Path), Is.EqualTo(new[] { Preview }));
    }

    [Test]
    public async Task Apply_DataImpactWithYes_ProceedsWithoutAsking()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(impacts: [StateMove(12)]));
        _cli.Server.On("POST", Propose, Detail(7), HttpStatusCode.Created);
        _cli.Server.On("POST", $"{Propose}/7/apply", Detail(7, "applied", 2));
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path, "--yes");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("已套用 #7"));
    }

    [TestCase("y", ExitCodes.Success)]
    [TestCase("YES", ExitCodes.Success)]
    [TestCase("n", ExitCodes.NeedsDecision)]
    [TestCase("", ExitCodes.NeedsDecision)]
    public async Task Apply_DataImpactInteractive_AsksAndFollowsTheAnswer(string answer, int expected)
    {
        // Arrange
        _cli.IsInteractive = true;
        _cli.StandardInput = answer + "\n";
        _cli.Server.On("POST", Preview, Plan(impacts: [StateMove(12)]));
        _cli.Server.On("POST", Propose, Detail(8), HttpStatusCode.Created);
        _cli.Server.On("POST", $"{Propose}/8/apply", Detail(8, "applied", 2));
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(expected), result.Error);
        Assert.That(result.Error, Does.Contain("要套用嗎？"));
        Assert.That(_cli.Server.To("POST", Propose), expected == ExitCodes.Success ? Has.Count.EqualTo(1) : Is.Empty);
    }

    [Test]
    public async Task Apply_NoChanges_DoesNotSubmitAnything()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(changes: []));
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(result.Out, Does.Contain("不需要套用"));
        Assert.That(_cli.Server.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Apply_Summary_IsPutIntoTheDocumentAndSentAsJsonForBothCalls()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(canApply: false));
        _cli.Server.On("POST", Propose, Detail(9), HttpStatusCode.Created);
        var path = WriteFile("apply.yaml", "# project: WEB\nbaseVersion: 3\nsummary: 舊的說明\nblueprint:\n  metadata: { key: web }\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path, "--summary", "新增「待驗收」狀態");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        foreach (var request in _cli.Server.Requests)
        {
            var body = JsonNode.Parse(request.Body)!;
            Assert.That(request.ContentType, Is.EqualTo("application/json"));
            Assert.That((string?)body["summary"], Is.EqualTo("新增「待驗收」狀態"));
            Assert.That((long?)body["baseVersion"], Is.EqualTo(3));
            Assert.That((string?)body["blueprint"]!["metadata"]!["key"], Is.EqualTo("web"));
        }

        Assert.That(_cli.Server.Requests[0].Body, Does.Contain("新增「待驗收」狀態"), "中文不轉成 \\u 跳脫");
    }

    [Test]
    public async Task Apply_SummaryWithAnUnreadableDocument_IsAUsageError()
    {
        // Arrange
        var path = WriteFile("apply.yaml", "# project: WEB\nbaseVersion: [1\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path, "--summary", "說明");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(_cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task Apply_Json_PrintsTheFinalChangeSetOrThePreviewWhenItStopsEarly()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(impacts: [StateMove(1)]));
        _cli.Server.On("POST", Propose, Detail(3), HttpStatusCode.Created);
        _cli.Server.On("POST", $"{Propose}/3/apply", Detail(3, "applied", 2));
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var stopped = await _cli.RunAsync("blueprint", "apply", "-f", path, "--json");
        var applied = await _cli.RunAsync("blueprint", "apply", "-f", path, "--json", "--yes");

        // Assert
        Assert.That(stopped.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision));
        Assert.That(JsonDocument.Parse(stopped.Out).RootElement.GetProperty("impacts").GetArrayLength(), Is.EqualTo(1));
        Assert.That(applied.ExitCode, Is.EqualTo(ExitCodes.Success), applied.Error);
        Assert.That(JsonDocument.Parse(applied.Out).RootElement.GetProperty("changeSet").GetProperty("status").GetString(), Is.EqualTo("applied"));
    }

    [Test]
    public async Task Apply_TokenWithoutBlueprintWriteScope_ExitsWithOneAndRelaysTheScopeMessage()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(canApply: true));
        _cli.Server.OnError("POST", Propose, HttpStatusCode.Forbidden, "auth.insufficient_scope", "這把權杖沒有 blueprint:write 範圍。");
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("blueprint:write"));
    }

    [Test]
    public async Task Apply_InvalidDocument_ExitsWithTwoBeforeSubmitting()
    {
        // Arrange
        _cli.Server.OnError("POST", Preview, HttpStatusCode.UnprocessableEntity, "changeset.invalid", "變更集沒有通過檢查。", ("/blueprint/states/0", "狀態重複。"));
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("狀態重複。"));
        Assert.That(_cli.Server.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Apply_ApplyIsBlockedByDataThatChangedMeanwhile_ExitsWithThree()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(canApply: true));
        _cli.Server.On("POST", Propose, Detail(4), HttpStatusCode.Created);
        _cli.Server.OnError("POST", $"{Propose}/4/apply", HttpStatusCode.Conflict, "changeset.apply_blocked", "變更集 #4 現在還不能套用。");
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision));
    }

    [Test]
    public void DocumentText_FormatFollowsTheExtensionAndOtherwiseTheFirstCharacter()
    {
        // Arrange
        var odd = WriteFile("doc.txt", "  {\"a\":1}");
        var yamlish = WriteFile("doc.cfg", "a: 1");
        var yml = WriteFile("doc.yml", "{\"a\":1}");

        // Act
        var formats = new[] { odd, yamlish, yml }
            .Select(path => DocumentText.ReadAsync(path, TextReader.Null, CancellationToken.None).GetAwaiter().GetResult().Format)
            .ToArray();

        // Assert
        Assert.That(formats, Is.EqualTo(new[] { DocumentFormat.Json, DocumentFormat.Yaml, DocumentFormat.Yaml }));
    }
}
