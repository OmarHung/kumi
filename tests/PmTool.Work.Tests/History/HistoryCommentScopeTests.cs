using PmTool.Abstractions;
using PmTool.Work.Tests.OwnItems;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.History;

/// <summary>
/// 歷史留言掛在只看自己項目的人看不到的項目上：和「項目沒匯入過」同一個回答，不帶項目編號，什麼都不寫；
/// 自己的項目照常匯入，真的刪除而且看得到的項目才說已經刪除。
/// </summary>
[TestFixture]
public sealed class HistoryCommentScopeTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ImportCommentsAsync_ItemTheCallerCannotSee_GetsTheSameAnswerAsAnItemNeverImportedAndWritesNothing()
    {
        // Arrange
        var world = await ScopedManagerAsync();
        var hidden = await ImportAsync(world, Row("h-1", world.Other.PersonId));

        // Act
        var forHidden = await _host.History.ImportCommentsAsync(
            world.Manager, "WEB", Comments(Comment("u-1", "h-1", world.Other.PersonId)), CancellationToken.None);
        var forNever = await _host.History.ImportCommentsAsync(
            world.Manager, "WEB", Comments(Comment("u-1", "nope", world.Other.PersonId)), CancellationToken.None);
        var written = await _host.History.LookupAsync(world.Scenario.Admin, Lookup("u-1"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((forHidden.Failure?.Code, Paths(forHidden.Failure)), Is.EqualTo((WorkErrorCodes.ImportInvalid, new[] { "comments/0/item_external_id" })));
            Assert.That(Messages(forHidden.Failure), Is.EqualTo(Messages(forNever.Failure).Replace("nope", "h-1", StringComparison.Ordinal)));
            Assert.That(Messages(forHidden.Failure), Does.Contain("要先匯入").And.Not.Contain(hidden).And.Not.Contain("已經刪除"));
            Assert.That(written.Value, Is.Empty);
        }
    }

    [Test]
    public async Task ImportCommentsAsync_DeletedItemTheCallerCannotSee_GetsTheSameAnswerAsAnItemNeverImported()
    {
        // Arrange
        var world = await ScopedManagerAsync();
        var hidden = await ImportAsync(world, Row("h-1", world.Other.PersonId));
        await _host.Items.DeleteAsync(world.Scenario.Admin, hidden, CancellationToken.None);

        // Act
        var result = await _host.History.ImportCommentsAsync(
            world.Manager, "WEB", Comments(Comment("u-1", "h-1", world.Other.PersonId)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Messages(result.Failure), Does.Contain("要先匯入").And.Not.Contain(hidden).And.Not.Contain("已經刪除"));
        }
    }

    [Test]
    public async Task ImportCommentsAsync_ItemTheCallerCreatedOrIsAssignedTo_IsImported()
    {
        // Arrange
        var world = await ScopedManagerAsync();
        await ImportAsync(world, Row("c-1", world.Manager.PersonId), Row("a-1", world.Other.PersonId) with
        {
            Item = new NewWorkItem { Type = "task", Title = "指派給他", AssigneeId = world.Manager.PersonId },
        });

        // Act
        var result = await _host.History.ImportCommentsAsync(
            world.Manager, "WEB", Comments(Comment("u-1", "c-1", world.Other.PersonId), Comment("u-2", "a-1", world.Other.PersonId)), CancellationToken.None);

        // Assert
        Assert.That(result.Value.Created.Select(c => c.ExternalId), Is.EqualTo(new[] { "u-1", "u-2" }));
    }

    [Test]
    public async Task ImportCommentsAsync_DeletedItemTheCallerCanSee_SaysItIsDeleted()
    {
        // Arrange
        var world = await ScopedManagerAsync();
        var own = await ImportAsync(world, Row("c-1", world.Manager.PersonId));
        await _host.Items.DeleteAsync(world.Scenario.Admin, own, CancellationToken.None);

        // Act
        var result = await _host.History.ImportCommentsAsync(
            world.Manager, "WEB", Comments(Comment("u-1", "c-1", world.Other.PersonId)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "comments/0/item_external_id" }));
            Assert.That(Messages(result.Failure), Does.Contain("已經刪除"));
        }
    }

    private sealed record World(Scenario Scenario, Actor Manager, Actor Other);

    /// <summary>呼叫者有 project.manage、但只看得到自己負責或建立的項目（沒有 item.read）；Other 是另一位成員。</summary>
    private async Task<World> ScopedManagerAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await OwnItemsWorld.PublishAsync(_host, scenario, "WEB", blueprint => blueprint with
        {
            Roles = [.. blueprint.Roles, OwnItemsWorld.Role("manager_own", WorkPermissions.ProjectManage, WorkPermissions.ItemReadOwn)],
        });
        var manager = await scenario.MemberAsync("WEB", "manager_own");
        var other = await scenario.MemberAsync("WEB", "editor");
        return new World(scenario, manager, other);
    }

    /// <summary>由組織管理員匯入項目，回第一個的項目編號。</summary>
    private async Task<string> ImportAsync(World world, params HistoryItem[] rows)
    {
        var result = await _host.History.ImportItemsAsync(world.Scenario.Admin, "WEB", Batch(rows), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value.Created[0].ItemKey;
    }

    private static HistoryLookup Lookup(string id) => new() { Source = Source, Kind = HistoryRefKind.Comment, ExternalIds = [id] };

    private static string Messages(Failure? failure) =>
        string.Join("\n", [failure?.Message, .. failure?.Details.Select(d => d.Message) ?? []]);
}
