using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class JobQuotaTests
{
    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);

    private string _directory = null!;
    private string _configPath = null!;
    private DateTimeOffset _now;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"kumi-quota-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _configPath = Path.Combine(_directory, "config.json");
        _now = new DateTimeOffset(2026, 11, 26, 10, 0, 0, TaipeiOffset);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private JobQuota Quota(int perHour = 30, int perDay = 200) => new(_configPath, perHour, perDay, () => _now);

    [Test]
    public void TryReserve_WhenTheHourlyLimitIsReached_RefusesAndDoesNotCount()
    {
        // Arrange
        var quota = Quota(perHour: 2);

        // Act
        var results = new[] { quota.TryReserve(), quota.TryReserve(), quota.TryReserve() };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results, Is.EqualTo(new[] { true, true, false }));
            Assert.That(quota.Remaining(), Is.Zero);
            Assert.That(quota.ExhaustedReason(), Does.Contain("每小時 2 件"));
            Assert.That(JobQuota.Read(_configPath, () => _now).Hour.Used, Is.EqualTo(2));
        }
    }

    [Test]
    public void Remaining_WhenAnHourPasses_TheHourlyWindowFreesUpButTheDailyOneDoesNot()
    {
        // Arrange
        var quota = Quota(perHour: 1, perDay: 2);
        quota.TryReserve();

        // Act
        _now = _now.AddMinutes(61);
        var afterAnHour = quota.Remaining();
        quota.TryReserve();
        _now = _now.AddMinutes(61);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterAnHour, Is.EqualTo(1));
            Assert.That(quota.Remaining(), Is.Zero, "一天兩件都用掉了");
            Assert.That(quota.ExhaustedReason(), Does.Contain("每天 2 件"));
        }
    }

    [Test]
    public void Remaining_WhenTheLocalDateChanges_TheDailyCountStartsOver()
    {
        // Arrange（本機時間 23:30 領一件，過了午夜算新的一天）
        _now = new DateTimeOffset(2026, 11, 26, 23, 30, 0, TaipeiOffset);
        var quota = Quota(perHour: 0, perDay: 1);
        quota.TryReserve();

        // Act
        _now = _now.AddHours(1);

        // Assert
        Assert.That(quota.Remaining(), Is.EqualTo(1));
    }

    [Test]
    public void Remaining_WithZeroLimits_IsUnlimited()
    {
        // Arrange
        var quota = Quota(perHour: 0, perDay: 0);

        // Act
        for (var count = 0; count < 500; count++)
        {
            quota.TryReserve();
        }

        // Assert
        Assert.That(quota.Remaining(), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void TryReserve_AfterARestart_KeepsTheCount()
    {
        // Arrange
        Quota(perHour: 3).TryReserve();
        Quota(perHour: 3).TryReserve();

        // Act
        var restarted = Quota(perHour: 3);

        // Assert
        Assert.That(restarted.Remaining(), Is.EqualTo(1));
    }

    [Test]
    public void Read_WithoutAFile_UsesTheDefaultLimits()
    {
        // Act
        var usage = JobQuota.Read(_configPath, () => _now);

        // Assert
        Assert.That(usage, Is.EqualTo(new QuotaUsage(new QuotaWindow(0, 30), new QuotaWindow(0, 200))));
    }

    [Test]
    public void Read_ShowsTheLimitsTheAgentLastStartedWith()
    {
        // Arrange
        Quota(perHour: 5, perDay: 0).TryReserve();

        // Act
        var usage = JobQuota.Read(_configPath, () => _now);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(usage.Hour, Is.EqualTo(new QuotaWindow(1, 5)));
            Assert.That(usage.Day, Is.EqualTo(new QuotaWindow(1, 0)));
            Assert.That(usage.Day.IsUnlimited, Is.True);
        }
    }

    [Test]
    public void Quota_WithACorruptFile_AssumesTheLimitsAreAlreadyUsedKeepsTheBrokenFileAndLogsIt()
    {
        // Arrange
        var logged = new List<string>();
        File.WriteAllText(JobQuota.PathFor(_configPath), "{ 不是 JSON");

        // Act
        var quota = new JobQuota(_configPath, 5, 20, () => _now, logged.Add);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(quota.Remaining(), Is.Zero, "壞檔不能當作沒用過");
            Assert.That(quota.TryReserve(), Is.False);
            Assert.That(logged, Has.Count.EqualTo(1));
            Assert.That(logged[0], Does.Contain("agent-usage.json").And.Contain("壞"));
            Assert.That(Directory.GetFiles(_directory, "agent-usage.json.corrupt*"), Has.Length.EqualTo(1));
        }
    }

    [Test]
    public void Quota_WithACorruptFile_StaysBlockedAfterARestartButNotWhenUnlimited()
    {
        // Arrange
        File.WriteAllText(JobQuota.PathFor(_configPath), "[1, 2");
        _ = Quota(perHour: 5, perDay: 20);

        // Act
        var restarted = Quota(perHour: 5, perDay: 20);
        File.WriteAllText(JobQuota.PathFor(_configPath), "garbage");
        var unlimited = Quota(perHour: 0, perDay: 0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restarted.Remaining(), Is.Zero);
            Assert.That(unlimited.Remaining(), Is.EqualTo(int.MaxValue));
        }
    }

    [Test]
    public void TryReserve_WritesThroughATempFileAndLeavesNoneBehind()
    {
        // Act
        Quota().TryReserve();
        Quota().TryReserve();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(_directory).Select(Path.GetFileName), Is.EquivalentTo(new[] { "agent-usage.json" }));
            Assert.That(JobQuota.Read(_configPath, () => _now).Hour.Used, Is.EqualTo(2));
        }
    }

    [Test]
    public void Read_WhenTheAgentIsNotRunning_ShowsTheDefaultLimitsAndSaysSo()
    {
        // Arrange
        Quota(perHour: 5, perDay: 0).TryReserve();

        // Act
        var running = JobQuota.Read(_configPath, () => _now, agentRunning: true);
        var stopped = JobQuota.Read(_configPath, () => _now, agentRunning: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(running.Hour.Limit, Is.EqualTo(5));
            Assert.That(running.LimitsAreDefaults, Is.False);
            Assert.That(stopped.Hour, Is.EqualTo(new QuotaWindow(1, JobQuota.DefaultPerHour)));
            Assert.That(stopped.LimitsAreDefaults, Is.True);
        }
    }

    [Test]
    public void Quota_RecordsItsLimitsAtStartSoStatusShowsThisRunsValues()
    {
        // Act
        _ = Quota(perHour: 7, perDay: 9);

        // Assert
        Assert.That(JobQuota.Read(_configPath, () => _now, agentRunning: true).Hour.Limit, Is.EqualTo(7));
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void TryReserve_WritesTheUsageFileReadableOnlyByTheOwner()
    {
        // Act
        Quota().TryReserve();

        // Assert
        Assert.That(File.GetUnixFileMode(JobQuota.PathFor(_configPath)), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }
}
