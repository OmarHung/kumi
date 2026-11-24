using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>批次操作的請求檢查與整批的權限：請求不合格是 item.bulk_invalid，沒有權限整批 Forbidden、一筆都不做。</summary>
[TestFixture]
public sealed class BulkRequestTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_NoKeys_IsBulkInvalidForKeys()
    {
        // Arrange
        var scenario = await ProjectAsync();

        // Act
        var result = await _host.Bulk.UpdateAsync(scenario.Admin, "WEB", new BulkItemUpdate { Keys = [], Priority = Priority.High }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.BulkInvalid, "keys");
    }

    [Test]
    public async Task UpdateAsync_MoreThanTheMaximum_IsBulkInvalidForKeysAndTouchesNothing()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var item = await scenario.CreateTaskAsync("WEB");
        var keys = Enumerable.Range(1, BulkItemUpdate.MaxItems + 1).Select(n => $"WEB-{n}").ToArray();

        // Act
        var result = await _host.Bulk.UpdateAsync(scenario.Admin, "WEB", new BulkItemUpdate { Keys = keys, Priority = Priority.High }, CancellationToken.None);
        var read = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.BulkInvalid, "keys");
        Assert.That(read.Value.Priority, Is.EqualTo(Priority.None));
    }

    [Test]
    public async Task UpdateAsync_ExactlyTheMaximum_IsAccepted()
    {
        // Arrange：兩百個編號，大部分不存在，各自算失敗，請求本身合格。
        var scenario = await ProjectAsync();
        var keys = Enumerable.Range(1, BulkItemUpdate.MaxItems).Select(n => $"WEB-{n}").ToArray();

        // Act
        var result = await _host.Bulk.UpdateAsync(scenario.Admin, "WEB", new BulkItemUpdate { Keys = keys, Priority = Priority.High }, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Failed, Has.Count.EqualTo(BulkItemUpdate.MaxItems));
    }

    [Test]
    public async Task UpdateAsync_StateTogetherWithAnotherProperty_IsBulkInvalidForState()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin, "WEB", new BulkItemUpdate { Keys = [item.Key], State = "doing", Priority = Priority.High }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.BulkInvalid, "state");
    }

    [Test]
    public async Task UpdateAsync_NothingToChange_IsBulkInvalidForTheRoot()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Bulk.UpdateAsync(scenario.Admin, "WEB", new BulkItemUpdate { Keys = [item.Key] }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.BulkInvalid, string.Empty);
    }

    [Test]
    public async Task UpdateAsync_StateGivenAsNull_IsBulkInvalidForState()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Bulk.UpdateAsync(scenario.Admin, "WEB", new BulkItemUpdate { Keys = [item.Key], State = new Optional<string>(null!) }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.BulkInvalid, "state");
    }

    [TestCase("delete")]
    [TestCase("restore")]
    public async Task DeleteAndRestoreAsync_NoKeys_IsBulkInvalidForKeys(string operation)
    {
        // Arrange
        var scenario = await ProjectAsync();

        // Act
        var result = operation == "delete"
            ? Wrap(await _host.Bulk.DeleteAsync(scenario.Admin, "WEB", [], CancellationToken.None))
            : Wrap(await _host.Bulk.RestoreAsync(scenario.Admin, "WEB", [], CancellationToken.None));

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.BulkInvalid, "keys");
    }

    [Test]
    public async Task BulkOperations_ProjectThatDoesNotExistOrTheCallerCannotSee_AreProjectNotFound()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var item = await scenario.CreateTaskAsync("WEB");
        var outsider = scenario.Person();

        // Act
        var missing = await _host.Bulk.DeleteAsync(scenario.Admin, "NOPE", [item.Key], CancellationToken.None);
        var hiddenUpdate = await _host.Bulk.UpdateAsync(outsider, "WEB", new BulkItemUpdate { Keys = [item.Key], Priority = Priority.High }, CancellationToken.None);
        var hiddenRestore = await _host.Bulk.RestoreAsync(outsider, "WEB", [item.Key], CancellationToken.None);

        // Assert
        ItemAssert.Failed(missing, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
        ItemAssert.Failed(hiddenUpdate, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
        ItemAssert.Failed(hiddenRestore, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    [TestCase("commenter")]
    [TestCase("viewer")]
    public async Task UpdateAsync_RoleWithoutItemUpdate_IsForbiddenForTheWholeBatchAndChangesNothing(string role)
    {
        // Arrange
        var scenario = await ProjectAsync();
        var member = await scenario.MemberAsync("WEB", role);
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Bulk.UpdateAsync(member, "WEB", new BulkItemUpdate { Keys = [item.Key], Priority = Priority.High }, CancellationToken.None);
        var read = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(result.Failure!.Message, Does.Contain("修改項目"));
        Assert.That(read.Value.Priority, Is.EqualTo(Priority.None));
    }

    [Test]
    public async Task UpdateAsync_StateChangeWithoutItemTransition_IsForbiddenForTheWholeBatch()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var member = await scenario.MemberAsync("WEB", "commenter");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Bulk.UpdateAsync(member, "WEB", new BulkItemUpdate { Keys = [item.Key], State = "doing" }, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(result.Failure!.Message, Does.Contain("改項目狀態"));
    }

    [Test]
    public async Task DeleteAndRestoreAsync_RoleWithoutItemDelete_AreForbiddenForTheWholeBatch()
    {
        // Arrange：接案專案的專案經理能改項目，但沒有 item.delete。
        var scenario = await ProjectAsync(Scenario.AgencyProject);
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        var live = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "留著" });
        var deleted = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "刪掉" });
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, deleted.Key);

        // Act
        var delete = await _host.Bulk.DeleteAsync(manager, "WEB", [live.Key], CancellationToken.None);
        var restore = await _host.Bulk.RestoreAsync(manager, "WEB", [deleted.Key], CancellationToken.None);
        var stillThere = await _host.Items.GetAsync(scenario.Admin, live.Key, CancellationToken.None);

        // Assert
        ItemAssert.Failed(delete, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        ItemAssert.Failed(restore, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(stillThere.Succeeded, Is.True);
    }

    private static Result<int> Wrap<T>(Result<T> result) => result.Failure is { } failure ? failure : 0;

    private async Task<Scenario> ProjectAsync(string template = Scenario.SimpleTasks)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", template);
        return scenario;
    }
}
