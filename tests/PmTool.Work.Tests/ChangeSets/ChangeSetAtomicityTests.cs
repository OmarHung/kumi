using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>
/// 套用是一整筆交易：項目改寫之後、活動寫不進去時，藍圖版本、項目、活動、封存、變更集的狀態全部退回。
/// 寫不進去靠測試資料庫上的觸發器做出來，只在 Act 期間存在（做法同 ActivityAtomicityTests）。
/// </summary>
[TestFixture]
public sealed class ChangeSetAtomicityTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ApplyAsync_WhenTheActivitiesCannotBeWritten_ChangesNothing()
    {
        // Arrange：WEB-1 在進行中而且有摘要；套用要把進行中拿掉、刪除摘要。另一份等待核准。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "範例任務", Fields = Scenario.Fields(("summary", "摘要")) });
        item = (await _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None)).Value;
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario,
            "WEB",
            b => b.WithoutState("task_flow", "doing").WithoutField("summary"),
            BlueprintEdits.Moves(("task_flow", "doing", "todo")) with { DropFields = ["summary"] });
        var other = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Views = [] });
        var activitiesBefore = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        Exception? thrown = null;
        await using (await ActivityWritesFailAsync())
        {
            try
            {
                await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
            }
            catch (DbUpdateException exception)
            {
                thrown = exception;
            }
        }

        var project = (await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None)).Value;
        var readBack = (await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None)).Value;
        var activitiesAfter = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);
        var listed = await _host.Changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrown, Is.Not.Null);
            Assert.That(project.Project.BlueprintVersion, Is.EqualTo(1));
            Assert.That(project.Blueprint.Fields.Select(f => f.Key), Does.Contain("summary"));
            Assert.That(readBack, Is.EqualTo(item with { Fields = readBack.Fields, Description = readBack.Description }));
            Assert.That(readBack.Fields["summary"].GetString(), Is.EqualTo("摘要"));
            Assert.That(activitiesAfter.Select(a => a.Id), Is.EqualTo(activitiesBefore.Select(a => a.Id)));
            Assert.That(listed.Value.Select(c => (c.Number, c.Status)), Is.EqualTo(new[] { (other.ChangeSet.Number, ChangeSetStatus.Pending), (proposed.ChangeSet.Number, ChangeSetStatus.Pending) }));
            Assert.That(await ArchivedValues.ListAsync(_host, scenario), Is.Empty);
        }

        // 失敗的那一次什麼都沒留下，同一份之後照樣套用得了。
        var retried = await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        Assert.That(retried.ChangeSet.AppliedVersion, Is.EqualTo(2));
    }

    /// <summary>之後新增的每一列活動都會失敗，直到回傳的物件被釋放。</summary>
    private async Task<IAsyncDisposable> ActivityWritesFailAsync()
    {
        await ExecuteAsync("""
            CREATE FUNCTION public.test_reject_migrated() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              RAISE EXCEPTION 'test: activity writes are blocked';
            END $$;
            CREATE TRIGGER test_reject_migrated BEFORE INSERT ON work.item_activities
              FOR EACH ROW EXECUTE FUNCTION public.test_reject_migrated();
            """);
        return new Undo(() => ExecuteAsync("""
            DROP TRIGGER test_reject_migrated ON work.item_activities;
            DROP FUNCTION public.test_reject_migrated();
            """));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class Undo(Func<Task> undo) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await undo();
    }
}
