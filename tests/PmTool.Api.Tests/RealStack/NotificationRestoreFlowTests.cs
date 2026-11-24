using Npgsql;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 備份還原到新的資料庫之後，新的交易編號比事件與通知讀者存的位置都小。
/// 模擬的方法：把兩邊的交易編號調成遠大於現在的，再新指派一筆，通知照樣要產生。
/// 自己一個類別（自己的資料庫），因為會改整張事件表。
/// </summary>
[TestFixture]
public sealed class NotificationRestoreFlowTests : RealStackTestBase
{
    private const long ForeignOffset = 5_000_000_000_000;

    [Test]
    public async Task Assignment_AfterTheDatabaseWasRestoredElsewhere_StillNotifiesTheAssignee()
    {
        // Arrange：還原前的一筆指派已經通知過，讀者位置存在舊資料庫的編號上。
        using var a = await SignInAsOwnerAsync();
        var email = RealStackSteps.UniqueEmail("member-b");
        await RealStackSteps.CreateMemberAsync(a.Client, "Member B Example", email);
        using var b = await SignInAsAsync(email, RealStackSteps.MemberPassword);
        await RealStackSteps.CreateProjectAsync(a.Client, "NTRST", "simple_tasks");
        await RealStackSteps.SetMemberAsync(a.Client, "NTRST", b.PersonId, "editor");
        await RealStackSteps.CreateItemAsync(a.Client, "NTRST", new { type = "task", title = "還原前", assigneeId = b.PersonId });
        Assert.That(await Factory.RunNotificationsOnceAsync(), Is.GreaterThan(0));
        await SimulateRestoreAsync();

        // Act
        await RealStackSteps.CreateItemAsync(a.Client, "NTRST", new { type = "task", title = "還原後", assigneeId = b.PersonId });
        var handed = await Factory.RunNotificationsOnceAsync();

        // Assert
        var page = await RealStackSteps.GetAsync(b.Client, "/v1/notifications?limit=100");
        var titles = page["items"]!.AsArray().Select(n => n!["itemTitle"]?.GetValue<string>()).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handed, Is.GreaterThan(0), "還原後的新事件要交給通知");
            Assert.That(titles, Is.EquivalentTo(new[] { "還原前", "還原後" }));
        }
    }

    private async Task SimulateRestoreAsync()
    {
        await using var connection = new NpgsqlConnection(Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        const string shift = "((transaction_id::text::bigint) + 5000000000000)";
        await using var command = new NpgsqlCommand(
            $"UPDATE work.events SET transaction_id = {shift}::text::xid8; UPDATE notifications.reader_positions SET transaction_id = transaction_id + {ForeignOffset};",
            connection);
        await command.ExecuteNonQueryAsync();
    }
}
