using Npgsql;

namespace PmTool.Work.Tests.Events;

/// <summary>
/// 事件流的讀法：依（交易編號、流水號）往後讀，只讀已經確定的交易。
/// 重點是先拿到交易編號、卻晚提交的寫入不會被跳過。
/// </summary>
[TestFixture]
public sealed class EventStreamTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TearDown]
    public void ClosePooledConnections() => NpgsqlConnection.ClearAllPools();

    [Test]
    public async Task ReadAfterAsync_ATransactionCommittedLaterThanOneThatStartedAfterIt_IsNotSkipped()
    {
        // Arrange：A 先拿到交易編號、還沒寫事件；B 之後才開始，卻先寫完並提交。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await using var slow = await SlowWriter.BeginAsync(_host.ConnectionString, scenario.OrganizationId);
        var item = await scenario.CreateTaskAsync("WEB", "B 的項目");

        // Act：A 還沒提交時讀。
        var whileOpen = await EventFeed.OfAsync(_host, scenario);
        await slow.WriteEventAndCommitAsync("A 的事件");
        var afterCommit = await EventFeed.OfAsync(_host, scenario);

        // Assert：先不給 B（否則游標往前走了，A 之後就讀不到）；A 提交之後兩筆都在，依交易編號排，A 在前。
        Assert.That(whileOpen, Is.Empty);
        Assert.That(afterCommit.Select(e => e.Item.Title), Is.EqualTo(new[] { "A 的事件", item.Title }));
        Assert.That(afterCommit[0].Position.Sequence, Is.GreaterThan(afterCommit[1].Position.Sequence), "A 的流水號比較大，只用流水號讀就會漏掉它");
        Assert.That(afterCommit[0].Position, Is.LessThan(afterCommit[1].Position));
    }

    [Test]
    public async Task ReadAfterAsync_ResumingFromTheLastPositionReadBeforeTheSlowTransactionCommits_StillGetsBoth()
    {
        // Arrange：讀的人在 A 還沒提交時，已經讀過更早的事件、記下位置。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var earlier = await scenario.CreateTaskAsync("WEB", "更早的項目");
        var consumed = (await EventFeed.OfAsync(_host, scenario)).Single().Position;
        await using var slow = await SlowWriter.BeginAsync(_host.ConnectionString, scenario.OrganizationId);
        await scenario.CreateTaskAsync("WEB", "B 的項目");

        // Act：從記下的位置接著讀，A 提交之前與之後各讀一次。
        var whileOpen = await _host.Events.ReadAfterAsync(consumed, 100, CancellationToken.None);
        await slow.WriteEventAndCommitAsync("A 的事件");
        var afterCommit = await _host.Events.ReadAfterAsync(consumed, 100, CancellationToken.None);

        // Assert
        Assert.That(earlier.Title, Is.EqualTo("更早的項目"));
        Assert.That(whileOpen, Is.Empty);
        Assert.That(afterCommit.Select(e => e.Item.Title), Is.EqualTo(new[] { "A 的事件", "B 的項目" }));
    }

    [Test]
    public async Task ReadAfterAsync_AnAbortedTransaction_LeavesNoEventAndDoesNotBlockLaterOnes()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await using (var aborted = await SlowWriter.BeginAsync(_host.ConnectionString, scenario.OrganizationId))
        {
            await aborted.WriteEventAndRollbackAsync("不會出現的事件");
        }

        // Act
        await scenario.CreateTaskAsync("WEB", "之後的項目");

        // Assert
        Assert.That((await EventFeed.OfAsync(_host, scenario)).Select(e => e.Item.Title), Is.EqualTo(new[] { "之後的項目" }));
    }

    [Test]
    public async Task ReadAfterAsync_WithALimit_ReturnsThatManyInOrderAndResumesFromTheLast()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        foreach (var title in new[] { "一", "二", "三", "四", "五" })
        {
            await scenario.CreateTaskAsync("WEB", title);
        }

        var all = await EventFeed.OfAsync(_host, scenario);

        // Act：從這個情境第一筆之前接著讀，兩筆一批。
        var first = await _host.Events.ReadAfterAsync(Before(all[0].Position), 2, CancellationToken.None);
        var second = await _host.Events.ReadAfterAsync(first[^1].Position, 2, CancellationToken.None);

        // Assert：測試依序執行，這個情境的事件就是最後面的五筆，沒有別人的事件插在中間。
        Assert.That(first.Select(e => e.Item.Title), Is.EqualTo(new[] { "一", "二" }));
        Assert.That(second.Select(e => e.Item.Title), Is.EqualTo(new[] { "三", "四" }));
    }

    [Test]
    public async Task ReadAfterAsync_AfterTheLastEvent_ReturnsAnEmptyList()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB");
        var last = (await EventFeed.OfAsync(_host, scenario))[^1];
        var everything = await _host.Events.ReadAfterAsync(WorkEventPosition.Start, 100_000, CancellationToken.None);

        // Act
        var after = await _host.Events.ReadAfterAsync(everything[^1].Position, 10, CancellationToken.None);

        // Assert
        Assert.That(after, Is.Empty);
        Assert.That(last.Position, Is.LessThanOrEqualTo(everything[^1].Position));
    }

    [Test]
    public void ReadAfterAsync_LimitBelowOne_IsRejected() =>
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await _host.Events.ReadAfterAsync(WorkEventPosition.Start, 0, CancellationToken.None));

    [Test]
    public async Task ReadAfterAsync_FromTheStartInSmallBatches_ReadsEveryEventExactlyOnce()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "留言", null, CancellationToken.None);
        await _host.Items.TransitionAsync(scenario.Admin, item.Key, "done", CancellationToken.None);

        // Act：一次一筆，每次從上一筆的位置接著讀。
        var seen = new List<WorkEvent>();
        var position = WorkEventPosition.Start;
        while (await _host.Events.ReadAfterAsync(position, 1, CancellationToken.None) is [var next])
        {
            seen.Add(next);
            position = next.Position;
        }

        // Assert
        Assert.That(seen.Select(e => e.EventId), Is.Unique);
        Assert.That(seen.Where(e => e.OrganizationId == scenario.OrganizationId).Select(e => e.Kind), Is.EqualTo(new[]
        {
            WorkEventKind.ItemCreated, WorkEventKind.ItemCommented, WorkEventKind.ItemTransitioned,
        }));
    }

    private static WorkEventPosition Before(WorkEventPosition position) => position with { Sequence = position.Sequence - 1 };

    /// <summary>測試自己的連線：先取得交易編號、之後才寫事件，模擬先開始卻晚提交的寫入。</summary>
    [Test]
    public async Task ReadAfterAsync_KindThisVersionDoesNotKnow_IsReadAsUnknownAndTheNextEventStillComes()
    {
        // Arrange：新版寫了一種這一版不認得的事件，後面還有正常的事件。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await InsertRawAsync(scenario.OrganizationId, "item_archived", """{"project_key":"WEB"}""");
        var item = await scenario.CreateTaskAsync("WEB", "之後的項目");

        // Act
        var events = await EventFeed.OfAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(events.Select(e => e.Kind).TakeLast(2), Is.EqualTo(new[] { WorkEventKind.Unknown, WorkEventKind.ItemCreated }));
            Assert.That(events[^1].Item.Title, Is.EqualTo(item.Title));
        }
    }

    [Test]
    public async Task ReadAfterAsync_KnownKindWithUnreadableContent_IsReadAsUnknown()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await InsertRawAsync(scenario.OrganizationId, "item_updated", "\"不是物件\"");

        // Act
        var events = await EventFeed.OfAsync(_host, scenario);

        // Assert
        Assert.That(events[^1].Kind, Is.EqualTo(WorkEventKind.Unknown));
    }

    private async Task InsertRawAsync(Guid organizationId, string kind, string content)
    {
        await using var connection = new NpgsqlConnection(_host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO work.events (event_id, organization_id, kind, actor_id, occurred_at, content) VALUES (@event, @organization, @kind, @actor, now(), @content::jsonb)",
            connection);
        command.Parameters.AddWithValue("event", Guid.NewGuid());
        command.Parameters.AddWithValue("organization", organizationId);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("actor", Guid.NewGuid());
        command.Parameters.AddWithValue("content", content);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class SlowWriter : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;
        private readonly Guid _organizationId;

        private SlowWriter(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid organizationId)
        {
            _connection = connection;
            _transaction = transaction;
            _organizationId = organizationId;
        }

        public static async Task<SlowWriter> BeginAsync(string connectionString, Guid organizationId)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand("SELECT pg_current_xact_id()::text", connection, transaction);
            await command.ExecuteScalarAsync();
            return new SlowWriter(connection, transaction, organizationId);
        }

        public async Task WriteEventAndCommitAsync(string title)
        {
            await InsertAsync(title);
            await _transaction.CommitAsync();
        }

        public async Task WriteEventAndRollbackAsync(string title)
        {
            await InsertAsync(title);
            await _transaction.RollbackAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private async Task InsertAsync(string title)
        {
            const string sql = """
                INSERT INTO work.events (event_id, organization_id, kind, actor_id, occurred_at, content)
                VALUES (@event, @organization, 'item_updated', @actor, now(), @content::jsonb)
                """;
            await using var command = new NpgsqlCommand(sql, _connection, _transaction);
            command.Parameters.AddWithValue("event", Guid.NewGuid());
            command.Parameters.AddWithValue("organization", _organizationId);
            command.Parameters.AddWithValue("actor", Guid.NewGuid());
            command.Parameters.AddWithValue("content", System.Text.Json.JsonSerializer.Serialize(new { project_key = "WEB", item_key = "WEB-1", title }));
            await command.ExecuteNonQueryAsync();
        }
    }
}
