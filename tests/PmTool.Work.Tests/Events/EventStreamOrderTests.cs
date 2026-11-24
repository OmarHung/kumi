using System.Globalization;
using Npgsql;

namespace PmTool.Work.Tests.Events;

/// <summary>交易編號的位數變多（999 到 1000、9999 到 10000）時，事件仍然依數字順序讀，不是依文字順序。</summary>
[TestFixture]
public sealed class EventStreamOrderTests
{
    private WorkTestHost _host = null!;

    [SetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [TearDown]
    public async Task StopAsync()
    {
        await _host.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    [Test]
    public async Task ReadAfterAsync_TransactionIdsOfDifferentLengths_AreReadInNumericOrderEvenWithALimit()
    {
        // Arrange：把交易編號用到下一個十的次方，在邊界前後各寫一筆事件（編號 99…9 與 100…0）。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await using var connection = new NpgsqlConnection(_host.ConnectionString);
        await connection.OpenAsync();
        var boundary = await NextPowerOfTenAsync(connection);
        await BurnUntilAsync(connection, boundary - 1);
        await InsertEventAtAsync(connection, scenario.OrganizationId, "邊界之前");
        await InsertEventAtAsync(connection, scenario.OrganizationId, "邊界之後");

        // Act：一次讀一筆。依文字排序的話，"1000…" 會排在 "999…" 前面，先讀到的是邊界之後那一筆。
        var first = await _host.Events.ReadAfterAsync(WorkEventPosition.Start, 100_000, CancellationToken.None);
        var ours = first.Where(e => e.OrganizationId == scenario.OrganizationId && e.Item.Title.StartsWith("邊界", StringComparison.Ordinal)).ToArray();
        var one = await _host.Events.ReadAfterAsync(Before(ours[0].Position), 1, CancellationToken.None);

        // Assert
        Assert.That(ours.Select(e => e.Item.Title), Is.EqualTo(new[] { "邊界之前", "邊界之後" }));
        Assert.That(ours[0].Position.TransactionId, Is.LessThan(boundary));
        Assert.That(ours[1].Position.TransactionId, Is.GreaterThanOrEqualTo(boundary));
        Assert.That(one.Single().Item.Title, Is.EqualTo("邊界之前"));
        Assert.That(first.Select(e => e.Position), Is.Ordered, "整個清單要依位置排序");
    }

    private static WorkEventPosition Before(WorkEventPosition position) => position with { Sequence = position.Sequence - 1 };

    private static async Task<long> NextPowerOfTenAsync(NpgsqlConnection connection)
    {
        var current = await CurrentTransactionIdAsync(connection);
        long boundary = 10;
        while (boundary <= current)
        {
            boundary *= 10;
        }

        return boundary;
    }

    /// <summary>一個一個交易把編號用掉，直到下一個交易拿到的編號就是 <paramref name="target"/>。</summary>
    private static async Task BurnUntilAsync(NpgsqlConnection connection, long target)
    {
        while (await CurrentTransactionIdAsync(connection) < target - 1)
        {
        }
    }

    private static async Task<long> CurrentTransactionIdAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT pg_current_xact_id()::text", connection);
        return long.Parse((string)(await command.ExecuteScalarAsync())!, CultureInfo.InvariantCulture);
    }

    private static async Task InsertEventAtAsync(NpgsqlConnection connection, Guid organizationId, string title)
    {
        const string sql = """
            INSERT INTO work.events (event_id, organization_id, kind, actor_id, occurred_at, content)
            VALUES (@event, @organization, 'item_updated', @actor, now(), @content::jsonb)
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("event", Guid.NewGuid());
        command.Parameters.AddWithValue("organization", organizationId);
        command.Parameters.AddWithValue("actor", Guid.NewGuid());
        command.Parameters.AddWithValue("content", System.Text.Json.JsonSerializer.Serialize(new { project_key = "WEB", item_key = "WEB-1", title }));
        await command.ExecuteNonQueryAsync();
    }
}
