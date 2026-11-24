using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PmTool.Work.Tests.Activities;

/// <summary>
/// 活動和修改寫在同一個交易：活動寫不進去時，修改也要整筆退回，不能留下沒有紀錄的修改。
/// 寫不進去靠測試資料庫上的觸發器做出來。這是測試裡唯一動資料庫結構的地方，
/// 觸發器只在每個測試的 Act 期間存在，用完就拿掉，同一個資料庫的其他測試不受影響。
/// </summary>
[TestFixture]
public sealed class ActivityAtomicityTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_WhenTheActivityCannotBeWritten_KeepsNoItemAndDoesNotUseUpTheNumber()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        Exception? thrown;
        await using (await ActivityWritesFailAsync())
        {
            thrown = await CaughtAsync(() =>
                _host.Items.CreateAsync(scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "寫不進去" }, CancellationToken.None));
        }

        var list = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);
        var next = await scenario.CreateTaskAsync("WEB", "下一個");

        // Assert
        Assert.That(thrown, Is.InstanceOf<DbUpdateException>());
        Assert.That(list.Value.Items, Is.Empty);
        Assert.That(next.Key, Is.EqualTo("WEB-1"), "流水號跟著退回，下一個項目還是 1 號");
    }

    [Test]
    public async Task UpdateAsync_WhenTheActivityCannotBeWritten_LeavesTheItemUnchanged()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "原本", Description = "原本的說明" });
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var patch = new WorkItemPatch { Title = "新的", Description = "新的說明", Fields = Scenario.Fields(("summary", "新的摘要")) };

        // Act
        Exception? thrown;
        await using (await ActivityWritesFailAsync())
        {
            thrown = await CaughtAsync(() => _host.Items.UpdateAsync(scenario.Admin, item.Key, patch, CancellationToken.None));
        }

        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(thrown, Is.InstanceOf<DbUpdateException>());
        Assert.That(readBack.Value, Is.EqualTo(item with { Fields = readBack.Value.Fields }));
        Assert.That(readBack.Value.Fields, Is.Empty);
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task TransitionAsync_WhenTheActivityCannotBeWritten_KeepsTheOldState()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        Exception? thrown;
        await using (await ActivityWritesFailAsync())
        {
            thrown = await CaughtAsync(() => _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None));
        }

        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(thrown, Is.InstanceOf<DbUpdateException>());
        Assert.That((readBack.Value.State, readBack.Value.UpdatedAt), Is.EqualTo(("todo", item.UpdatedAt)));
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    /// <summary>之後新增的每一列活動都會失敗，直到回傳的物件被釋放。</summary>
    private async Task<IAsyncDisposable> ActivityWritesFailAsync()
    {
        await ExecuteAsync("""
            CREATE FUNCTION public.test_reject_activity() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              RAISE EXCEPTION 'test: activity writes are blocked';
            END $$;
            CREATE TRIGGER test_reject_activity BEFORE INSERT ON work.item_activities
              FOR EACH ROW EXECUTE FUNCTION public.test_reject_activity();
            """);
        return new Undo(() => ExecuteAsync("""
            DROP TRIGGER test_reject_activity ON work.item_activities;
            DROP FUNCTION public.test_reject_activity();
            """));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Exception?> CaughtAsync(Func<Task> call)
    {
        try
        {
            await call();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class Undo(Func<Task> undo) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await undo();
    }
}
