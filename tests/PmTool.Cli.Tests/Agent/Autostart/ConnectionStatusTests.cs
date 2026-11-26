using PmTool.Cli.Agent;
using PmTool.Cli.Agent.Autostart;

namespace PmTool.Cli.Tests.Agent.Autostart;

/// <summary>
/// <c>kumi agent status</c> 看得出背景程序是不是一直連不上伺服器，以及記錄檔不見了的時候怎麼辦。
/// 背景程序開機時連不上（例如要 VPN）會一直安靜地重試，記錄檔又被刪掉時，使用者原本完全看不出原因。
/// </summary>
[TestFixture]
public sealed class ConnectionStatusTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 26, 12, 21, 5, TimeSpan.FromHours(8));

    private string _config = null!;

    [SetUp]
    public void SetUp() => _config = Path.Combine(Path.GetTempPath(), $"kumi-conn-{Guid.NewGuid():N}", "kumi", "config.json");

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(_config))!, recursive: true);
        }
        catch (IOException)
        {
            // 暫時資料夾刪不掉不影響測試。
        }
    }

    private static Func<AgentConnection, bool> Recorder(List<AgentConnection> written) => connection =>
    {
        written.Add(connection);
        return true;
    };

    [Test]
    public void Tracker_WhenAWriteFails_WritesTheSameStateAgainNextTime()
    {
        // Arrange：Windows 上 status 正在讀資訊檔時，換檔會失敗。
        var attempts = new List<AgentConnection>();
        var succeeds = false;
        var tracker = new ConnectionTracker(connection =>
        {
            attempts.Add(connection);
            return succeeds;
        }, () => T0);

        // Act
        tracker.Succeeded();
        succeeds = true;
        tracker.Succeeded();
        tracker.Succeeded();

        // Assert
        Assert.That(attempts.Select(connection => connection.State), Is.EqualTo(new[]
        {
            AgentConnectionState.Connected, AgentConnectionState.Connected,
        }), "沒寫成的要補寫，寫成之後不再重寫");
    }

    [Test]
    public void Tracker_WhileItKeepsFailing_KeepsTheFirstFailureTimeAndUpdatesTheReason()
    {
        // Arrange
        var written = new List<AgentConnection>();
        var now = T0;
        var tracker = new ConnectionTracker(Recorder(written), () => now);

        // Act
        tracker.Connecting();
        now = T0.AddSeconds(75);
        tracker.Failed("連不上伺服器");
        now = T0.AddSeconds(150);
        tracker.Failed("連不上伺服器");
        now = T0.AddSeconds(230);
        tracker.Failed("伺服器暫時不能用");

        // Assert
        Assert.That(written, Is.EqualTo(new[]
        {
            new AgentConnection(AgentConnectionState.Connecting, T0, null),
            new AgentConnection(AgentConnectionState.Retrying, T0.AddSeconds(75), "連不上伺服器"),
            new AgentConnection(AgentConnectionState.Retrying, T0.AddSeconds(75), "伺服器暫時不能用"),
        }));
    }

    [Test]
    public void Tracker_AfterReconnecting_WritesConnectedOnceAndForgetsTheReason()
    {
        // Arrange
        var written = new List<AgentConnection>();
        var now = T0;
        var tracker = new ConnectionTracker(Recorder(written), () => now);
        tracker.Failed("連不上伺服器");

        // Act
        now = T0.AddMinutes(20);
        tracker.Succeeded();
        tracker.Succeeded();

        // Assert
        Assert.That(written[^1], Is.EqualTo(new AgentConnection(AgentConnectionState.Connected, T0.AddMinutes(20), null)));
        Assert.That(written, Has.Count.EqualTo(2), "一直連得上不必每次都寫");
    }

    [Test]
    public void Tracker_CutsAVeryLongReason()
    {
        // Arrange
        var written = new List<AgentConnection>();
        var tracker = new ConnectionTracker(Recorder(written), () => T0);

        // Act
        tracker.Failed(new string('x', 2000));

        // Assert
        Assert.That(written[0].Error!.Length, Is.LessThanOrEqualTo(ConnectionTracker.MaxErrorLength));
    }

    [Test]
    public void Lock_ReportedConnection_IsSeenByOthersAndGoneAfterRelease()
    {
        // Arrange
        var (held, _) = AgentLock.TryAcquire(_config, pid: 874, background: true);
        var connection = new AgentConnection(AgentConnectionState.Retrying, T0, "連不上伺服器");

        // Act
        var written = held!.Report(connection);
        var whileHeld = AgentLock.Peek(_config);
        held.Dispose();
        var afterRelease = AgentLock.Peek(_config);

        // Assert
        Assert.That(written, Is.True);
        Assert.That(whileHeld, Is.EqualTo(new AgentHolder(874, true, connection)));
        Assert.That(afterRelease, Is.Null);
    }

    [Test]
    public void Lock_ReportAfterDispose_DoesNotLeaveAStaleInfoFile()
    {
        // Arrange
        var (held, _) = AgentLock.TryAcquire(_config, pid: 874, background: true);
        held!.Dispose();

        // Act
        var written = held.Report(new AgentConnection(AgentConnectionState.Connected, T0, null));

        // Assert
        Assert.That(written, Is.False);
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(_config)!, AgentLock.InfoFileName)), Is.False);
    }
}
