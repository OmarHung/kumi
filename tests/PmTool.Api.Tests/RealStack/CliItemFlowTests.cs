using System.Text.Json;
using PmTool.Cli;
using PmTool.Identity;

namespace PmTool.Api.Tests.RealStack;

/// <summary>CLI 的項目與登入主線：用真的權杖、真的 API 與資料庫。</summary>
[TestFixture]
public sealed class CliItemFlowTests : RealStackTestBase
{
    [Test]
    public async Task CreateMoveAndList_AllowedAndDisallowedTransitions_GiveTheRightExitCodes()
    {
        // Arrange：接案專案的需求有轉換的限制：確認 → 報價 可以，確認 → 結案 不行。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "FLOW", "agency_project");
        using var cli = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);

        // Act
        var create = await cli.RunAsync(
            "item", "create", "--project", "FLOW", "--type", "requirement", "--title", "官網改版需求", "--assignee", "@me");
        var allowed = await cli.RunAsync("item", "move", "FLOW-1", "--to", "quoting");
        var disallowed = await cli.RunAsync("item", "move", "FLOW-1", "--to", "closed");
        var mine = await cli.RunAsync("item", "list", "--project", "FLOW", "--mine");
        var stillThere = await RealStackSteps.GetAsync(owner.Client, "/v1/items/FLOW-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(create.ExitCode, Is.EqualTo(ExitCodes.Success), create.Error);
            Assert.That(create.Out, Does.Contain("已建立 FLOW-1"));
            Assert.That(allowed.ExitCode, Is.EqualTo(ExitCodes.Success), allowed.Error);
            Assert.That(disallowed.ExitCode, Is.EqualTo(ExitCodes.Invalid), disallowed.Out);
            Assert.That(disallowed.Error, Does.Contain("錯誤："));
            Assert.That(mine.ExitCode, Is.EqualTo(ExitCodes.Success), mine.Error);
            Assert.That(mine.Out, Does.Contain("FLOW-1").And.Contain("官網改版需求").And.Contain("報價").And.Not.Contain("quoting").And.Contain(RealStackFactory.OwnerName), "給人看的是藍圖裡的名稱");
            Assert.That(stillThere["state"]!.GetValue<string>(), Is.EqualTo("quoting"), "不允許的轉換沒有生效");
        }
    }

    [Test]
    public async Task ItemCommands_UpdateCommentViewAndJson_ReflectWhatTheServerStored()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "ITEMS", "simple_tasks");
        using var cli = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);
        await cli.RunAsync("item", "create", "-p", "ITEMS", "--type", "task", "-t", "寫文件", "--set", "story_points=3", "--set", "priority=high");
        await cli.RunAsync("item", "create", "-p", "ITEMS", "--type", "task", "-t", "別人的", "--assignee", "none-such@example.com");

        // Act
        var update = await cli.RunAsync("item", "update", "ITEMS-1", "--title", "寫完文件", "--assignee", "@me", "--set", "budget=1200", "--set", "tags=[\"docs\"]");
        var comment = await cli.RunAsync("item", "comment", "ITEMS-1", "-m", "第一版完成");
        var view = await cli.RunAsync("item", "view", "ITEMS-1");
        var json = await cli.RunAsync("item", "list", "-p", "ITEMS", "--json", "--state", "todo");
        var clearAssignee = await cli.RunAsync("item", "update", "ITEMS-1", "--assignee", "none", "--json");
        var unknownField = await cli.RunAsync("item", "update", "ITEMS-1", "--set", "nope=1");

        // Assert
        var items = JsonDocument.Parse(json.Out).RootElement.GetProperty("items");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(update.ExitCode, Is.EqualTo(ExitCodes.Success), update.Error);
            Assert.That(comment.ExitCode, Is.EqualTo(ExitCodes.Success), comment.Error);
            Assert.That(view.Out, Does.Contain("ITEMS-1  寫完文件").And.Contain("負責人：Owner Example").And.Contain("預算：1200").And.Contain("留言：第一版完成"));
            Assert.That(view.Out, Does.Contain("點數：3").And.Contain("優先順序：高"));
            Assert.That(items.GetArrayLength(), Is.EqualTo(1), "第二個項目建立失敗：找不到那個負責人");
            Assert.That(clearAssignee.ExitCode, Is.EqualTo(ExitCodes.Success), clearAssignee.Error);
            Assert.That(JsonDocument.Parse(clearAssignee.Out).RootElement.TryGetProperty("assignee", out var assignee) && assignee.ValueKind == JsonValueKind.Null, Is.True);
            Assert.That(unknownField.ExitCode, Is.EqualTo(ExitCodes.Invalid), unknownField.Out);
        }
    }

    [Test]
    public async Task ItemCreate_AssigneeByEmail_UsesThePeopleList()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "EMAIL", "simple_tasks");
        using var cli = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);

        // Act
        var create = await cli.RunAsync(
            "item", "create", "-p", "EMAIL", "--type", "task", "-t", "指給擁有者", "--assignee", RealStackFactory.OwnerEmail.ToUpperInvariant());
        var item = await RealStackSteps.GetAsync(owner.Client, "/v1/items/EMAIL-1");

        // Assert
        Assert.That(create.ExitCode, Is.EqualTo(ExitCodes.Success), create.Error);
        Assert.That(item["assignee"]!["personId"]!.GetValue<Guid>(), Is.EqualTo(owner.PersonId));
    }

    [Test]
    public async Task Project_ListAndTokenWithoutItemsScope_ShowsProjectsOnlyWhenTheScopeAllows()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "SCOPED", "simple_tasks");
        using var reader = await CliSession.CreateAsync(Factory, owner, AccessTokenScope.ItemsRead);
        using var blueprintOnly = await CliSession.CreateAsync(Factory, owner, AccessTokenScope.BlueprintRead);

        // Act
        var allowed = await reader.RunAsync("project", "list");
        var refused = await blueprintOnly.RunAsync("project", "list");
        var cannotWrite = await reader.RunAsync("item", "create", "-p", "SCOPED", "--type", "task", "-t", "不行");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(allowed.ExitCode, Is.EqualTo(ExitCodes.Success), allowed.Error);
            Assert.That(allowed.Out, Does.Contain("SCOPED"));
            Assert.That(refused.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(refused.Error, Does.Contain("items:read"));
            Assert.That(cannotWrite.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(cannotWrite.Error, Does.Contain("items:write"));
        }
    }

    [Test]
    public async Task AuthLoginStatusLogout_AgainstARealServer_StoresChecksAndForgetsTheToken()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        using var cli = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);

        // Act
        var login = await cli.RunWithInputAsync(cli.Token.Secret, "auth", "login", "--server", CliSession.Server, "--with-token");
        var viaConfigFile = await cli.RunWithConfigFileAsync(string.Empty, "auth", "status");
        var logout = await cli.RunWithConfigFileAsync(string.Empty, "auth", "logout");
        var afterLogout = await cli.RunWithConfigFileAsync(string.Empty, "project", "list");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(login.ExitCode, Is.EqualTo(ExitCodes.Success), login.Error);
            Assert.That(login.Out, Does.Contain(RealStackFactory.OwnerName).And.Contain(RealStackFactory.OwnerEmail));
            Assert.That(login.Out + login.Error, Does.Not.Contain(cli.Token.Secret));
            Assert.That(viaConfigFile.ExitCode, Is.EqualTo(ExitCodes.Success), viaConfigFile.Error);
            Assert.That(viaConfigFile.Out, Does.Contain(RealStackFactory.OwnerName).And.Contain(cli.ConfigPath));
            Assert.That(logout.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(afterLogout.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(afterLogout.Error, Does.Contain("kumi auth login"));
        }
    }

    [Test]
    public async Task RevokedToken_IsRefusedWithAHintToLogInAgain()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        using var cli = await CliSession.CreateAsync(Factory, owner, CliSession.AllScopes);
        var before = await cli.RunAsync("project", "list");

        // Act
        var revoked = await cli.RevokeAsync();
        var after = await cli.RunAsync("project", "list");
        var login = await cli.RunWithInputAsync(cli.Token.Secret, "auth", "login", "--server", CliSession.Server, "--with-token");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.ExitCode, Is.EqualTo(ExitCodes.Success), before.Error);
            Assert.That(revoked.Succeeded, Is.True);
            Assert.That(after.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(after.Error, Does.Contain("權杖無效、已過期或已撤銷"));
            Assert.That(login.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(File.Exists(cli.ConfigPath), Is.False, "驗證沒過就不存");
        }
    }

    [Test]
    public async Task TokenOfAPersonWithoutAccess_CannotSeeTheProject()
    {
        // Arrange：權杖代表建立它的人；不是專案成員的人，權杖再大也看不到。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "HIDDEN", "simple_tasks");
        var person = await RealStackSteps.CreateMemberAsync(owner.Client, "Outsider Example", RealStackSteps.UniqueEmail("outsider"));
        using var outsider = await SignInAsAsync(person["email"]!.GetValue<string>(), RealStackSteps.MemberPassword);
        using var cli = await CliSession.CreateAsync(Factory, outsider, CliSession.AllScopes);

        // Act
        var list = await cli.RunAsync("item", "list", "--project", "HIDDEN");
        var export = await cli.RunAsync("blueprint", "export", "--project", "HIDDEN");

        // Assert
        Assert.That(list.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(export.ExitCode, Is.EqualTo(ExitCodes.Error));
    }
}
