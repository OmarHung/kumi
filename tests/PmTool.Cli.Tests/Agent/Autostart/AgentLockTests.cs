using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent.Autostart;

[TestFixture]
public sealed class AgentLockTests
{
    private string _config = null!;

    [SetUp]
    public void SetUp() => _config = Path.Combine(Path.GetTempPath(), $"kumi-lock-{Guid.NewGuid():N}", "kumi", "config.json");

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

    [Test]
    public void SecondAcquire_WhileTheFirstIsHeld_FailsAndSeesTheHolder()
    {
        // Arrange
        var (first, _) = AgentLock.TryAcquire(_config, pid: 111, background: true);

        // Act
        var (second, holder) = AgentLock.TryAcquire(_config, pid: 222, background: false);

        // Assert
        using (first)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Null);
            Assert.That(holder, Is.EqualTo(new AgentHolder(111, true)));
        }
    }

    [Test]
    public void AfterTheHolderReleases_TheLockCanBeTakenAgainAndNobodyIsReported()
    {
        // Arrange
        var (first, _) = AgentLock.TryAcquire(_config, pid: 111, background: false);
        first!.Dispose();

        // Act
        var peeked = AgentLock.Peek(_config);
        var (again, _) = AgentLock.TryAcquire(_config, pid: 333, background: true);

        // Assert
        using (again)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(peeked, Is.Null);
            Assert.That(again, Is.Not.Null);
        }
    }

    [Test]
    public void Peek_IgnoresStaleInfoLeftByACrashedHolder()
    {
        // Arrange：資訊檔還在、但沒有人拿著鎖（程序當掉）。
        Directory.CreateDirectory(Path.GetDirectoryName(_config)!);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_config)!, AgentLock.InfoFileName), """{"pid":999,"background":true}""");

        // Act
        var holder = AgentLock.Peek(_config);

        // Assert
        Assert.That(holder, Is.Null);
    }

    [Test]
    public void Peek_WhenHeld_ReturnsTheHolderWithoutDisturbingIt()
    {
        // Arrange
        var (held, _) = AgentLock.TryAcquire(_config, pid: 555, background: false);

        // Act
        var holder = AgentLock.Peek(_config);
        var stillHeld = AgentLock.TryAcquire(_config, pid: 666, background: true).Lock;

        // Assert
        using (held)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(holder, Is.EqualTo(new AgentHolder(555, false)));
            Assert.That(stillHeld, Is.Null);
        }
    }

    [Test]
    public void Peek_WithoutAConfigFolder_ReportsNobodyAndCreatesNothing()
    {
        // Act
        var holder = AgentLock.Peek(_config);

        // Assert
        Assert.That(holder, Is.Null);
        Assert.That(Directory.Exists(Path.GetDirectoryName(_config)), Is.False);
    }

    [Test]
    public async Task TryAcquireWithRetry_WhenSomeoneJustPeeked_WaitsAndThenGetsTheLock()
    {
        // Arrange（status／enable 的 Peek 只拿一下子：第二次嘗試前就放掉）
        var (first, _) = AgentLock.TryAcquire(_config, pid: 0, background: false);
        var delays = 0;

        // Act
        var (acquired, holder) = await AgentLock.TryAcquireWithRetryAsync(
            _config, pid: 222, background: true, attempts: 6, TimeSpan.FromSeconds(1),
            (_, _) =>
            {
                delays++;
                first!.Dispose();
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // Assert
        using (acquired)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(acquired, Is.Not.Null);
            Assert.That(holder, Is.Null);
            Assert.That(delays, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task TryAcquireWithRetry_WhenTheHolderStaysForever_GivesUpAfterTheAttempts()
    {
        // Arrange
        var (first, _) = AgentLock.TryAcquire(_config, pid: 111, background: true);
        var delays = 0;

        // Act
        var (acquired, holder) = await AgentLock.TryAcquireWithRetryAsync(
            _config, pid: 222, background: true, attempts: 6, TimeSpan.FromSeconds(1), (_, _) => { delays++; return Task.CompletedTask; }, CancellationToken.None);

        // Assert
        using (first)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(acquired, Is.Null);
            Assert.That(holder, Is.EqualTo(new AgentHolder(111, true)));
            Assert.That(delays, Is.EqualTo(5));
        }
    }

    [Test]
    public void Dispose_RemovesTheInfoFileSoNoStaleHolderIsLeftBehind()
    {
        // Arrange
        var (held, _) = AgentLock.TryAcquire(_config, pid: 111, background: true);

        // Act
        held!.Dispose();

        // Assert
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(_config)!, AgentLock.InfoFileName)), Is.False);
    }
}
