using System.Net;
using System.Text.Json;

namespace PmTool.Cli.Tests;

[TestFixture]
public sealed class ChangeSetCommandTests
{
    private const string Route = "/v1/projects/WEB/changesets";

    private static object Detail(string status = "pending") => new
    {
        changeSet = new
        {
            number = 3, status, baseVersion = 2, appliedVersion = status == "applied" ? 3 : (int?)null, summary = "新增狀態",
            proposedBy = new { displayName = "王小明" }, proposedAt = "2026-11-02T02:00:00Z",
        },
        changes = new[] { new { kind = "modified", resource = "state", key = "doing", parentKey = "main", changedProperties = new[] { "name", "color" } } },
        impacts = new[] { new { kind = "role_removal", affected = 1, blocksApply = true, role = "client" } },
        blockers = new[] { new { code = "changeset.members_with_removed_role", path = "impacts/0", message = "還有 1 位成員的角色是「客戶」。" } },
        canApply = false,
    };

    [Test]
    public async Task List_PrintsEachChangeSetWithItsStatusInChinese()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", Route, new[]
        {
            new { number = 3, status = "pending", baseVersion = 2, appliedVersion = (int?)null, summary = (string?)"新增狀態", proposedBy = new { displayName = "王小明" }, proposedAt = "2026-11-02T02:00:00Z" },
            new { number = 2, status = "applied", baseVersion = 1, appliedVersion = (int?)2, summary = (string?)null, proposedBy = new { displayName = "李小華" }, proposedAt = "2026-11-01T02:00:00Z" },
            new { number = 1, status = "expired", baseVersion = 1, appliedVersion = (int?)null, summary = (string?)null, proposedBy = new { displayName = "李小華" }, proposedAt = "2026-10-31T02:00:00Z" },
        });

        // Act
        var result = await cli.RunAsync("changeset", "list", "--project", "WEB");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("#3").And.Contain("等待核准").And.Contain("已套用").And.Contain("已過期").And.Contain("新增狀態"));
    }

    [Test]
    public async Task List_Json_PrintsTheDataAsIs()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", Route, Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("changeset", "list", "-p", "WEB", "--json");

        // Assert
        Assert.That(JsonDocument.Parse(result.Out).RootElement.GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task Show_PrintsHeaderDiffImpactsAndBlockers()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", $"{Route}/3", Detail());

        // Act
        var result = await cli.RunAsync("changeset", "show", "#3", "--project", "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("變更集 #3：等待核准").And.Contain("說明：新增狀態").And.Contain("王小明").And.Contain("基準版本：2"));
            Assert.That(result.Out, Does.Contain("~ 修改 狀態 doing（main 底下）：name、color"));
            Assert.That(result.Out, Does.Contain("角色 client 被刪除").And.Contain("還有 1 位成員的角色是「客戶」。"));
        }
    }

    [Test]
    public async Task Show_Json_PrintsTheDetailAsIs()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", $"{Route}/3", Detail());

        // Act
        var result = await cli.RunAsync("changeset", "show", "3", "-p", "WEB", "--json");

        // Assert
        Assert.That(JsonDocument.Parse(result.Out).RootElement.GetProperty("changeSet").GetProperty("number").GetInt32(), Is.EqualTo(3));
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("abc")]
    [TestCase("1.5")]
    public async Task Show_NumberThatIsNotAPositiveInteger_IsAUsageErrorWithoutARequest(string number)
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();

        // Act
        var result = await cli.RunAsync("changeset", "show", "--project", "WEB", "--", number);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task Show_NotFound_ExitsWithOne()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.OnError("GET", $"{Route}/9", HttpStatusCode.NotFound, "changeset.not_found", "找不到變更集 #9。");

        // Act
        var result = await cli.RunAsync("changeset", "show", "9", "-p", "WEB");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("找不到變更集 #9。"));
    }

    [Test]
    public async Task Apply_ReportsTheNewBlueprintVersion()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("POST", $"{Route}/3/apply", Detail("applied"));

        // Act
        var result = await cli.RunAsync("changeset", "apply", "3", "-p", "WEB");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("已套用 #3").And.Contain("第 3 版"));
    }

    [Test]
    public async Task Apply_Blocked_ExitsWithThreeAndShowsTheReasons()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.OnError("POST", $"{Route}/3/apply", HttpStatusCode.Conflict, "changeset.apply_blocked", "變更集 #3 現在還不能套用。", ("impacts/0", "還有 1 位成員的角色是「客戶」。"));

        // Act
        var result = await cli.RunAsync("changeset", "apply", "3", "-p", "WEB");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision));
        Assert.That(result.Error, Does.Contain("impacts/0：還有 1 位成員"));
    }

    [Test]
    public async Task Reject_ReportsTheRejectionAndJsonKeepsTheDetail()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("POST", $"{Route}/3/reject", Detail("rejected"));

        // Act
        var text = await cli.RunAsync("changeset", "reject", "3", "-p", "WEB");
        var json = await cli.RunAsync("changeset", "reject", "3", "-p", "WEB", "--json");

        // Assert
        Assert.That(text.Out, Does.Contain("已拒絕 #3"));
        Assert.That(JsonDocument.Parse(json.Out).RootElement.GetProperty("changeSet").GetProperty("status").GetString(), Is.EqualTo("rejected"));
    }

    [Test]
    public async Task Reject_NotPending_ExitsWithOne()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.OnError("POST", $"{Route}/3/reject", HttpStatusCode.Conflict, "changeset.not_pending", "變更集 #3 已經套用了，不能再套用或拒絕。");

        // Act
        var result = await cli.RunAsync("changeset", "reject", "3", "-p", "WEB");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
    }

    [Test]
    public async Task Apply_NumberWrittenWithAHash_IsAcceptedAndPrintedWithASingleHash()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("POST", $"{Route}/3/apply", Detail("applied"));
        cli.Server.On("POST", $"{Route}/3/reject", Detail("rejected"));

        // Act
        var applied = await cli.RunAsync("changeset", "apply", "#3", "-p", "WEB");
        var rejected = await cli.RunAsync("changeset", "reject", "#3", "-p", "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.ExitCode, Is.EqualTo(ExitCodes.Success), applied.Error);
            Assert.That(applied.Out, Does.Contain("已套用 #3").And.Not.Contain("##"));
            Assert.That(rejected.Out, Does.Contain("已拒絕 #3").And.Not.Contain("##"));
        }
    }
}
