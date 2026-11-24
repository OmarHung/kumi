using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>組織的時區：新組織的預設、讀得到設定的時區、壞掉的設定用預設並記警告、程序內快取 5 分鐘。</summary>
[TestFixture]
public sealed class OrganizationClockTests
{
    private const string Tokyo = "Asia/Tokyo";

    private ManualTimeProvider _time = null!;
    private WarningLog _log = null!;
    private IdentityTestHost _host = null!;
    private IOrganizationClock Clock => _host.Services.GetRequiredService<IOrganizationClock>();

    [SetUp]
    public async Task StartAsync()
    {
        _time = new ManualTimeProvider();
        _log = new WarningLog();
        _host = await IdentityTestHost.StartAsync(services =>
        {
            services.AddSingleton<TimeProvider>(_time);
            services.AddSingleton<ILogger<OrganizationClock>>(_log);
        });
    }

    [TearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task TimeZoneAsync_NewOrganization_IsTaipei()
    {
        // Arrange
        var owner = await _host.StartWithOwnerAsync();

        // Act
        var zone = await Clock.TimeZoneAsync(owner.OrganizationId, CancellationToken.None);

        // Assert
        Assert.That(zone.GetUtcOffset(DateTimeOffset.UtcNow), Is.EqualTo(TimeSpan.FromHours(8)));
        Assert.That(_log.Warnings, Is.Empty);
    }

    [Test]
    public async Task TimeZoneAsync_StoredZone_IsUsedForTheOrganizationsToday()
    {
        // Arrange
        var owner = await _host.StartWithOwnerAsync();
        await SetZoneAsync(owner.OrganizationId, "America/Los_Angeles");

        // Act
        var zone = await Clock.TimeZoneAsync(owner.OrganizationId, CancellationToken.None);
        var today = OrganizationTime.Today(zone, new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero));

        // Assert：UTC 11/2 03:00 在洛杉磯還是 11/1。
        Assert.That(today, Is.EqualTo(new DateOnly(2026, 11, 1)));
    }

    [TestCase("Nowhere/Land")]
    [TestCase("")]
    public async Task TimeZoneAsync_UnknownZone_FallsBackToTheDefaultAndWarns(string stored)
    {
        // Arrange
        var owner = await _host.StartWithOwnerAsync();
        await SetZoneAsync(owner.OrganizationId, stored);

        // Act
        var zone = await Clock.TimeZoneAsync(owner.OrganizationId, CancellationToken.None);

        // Assert
        Assert.That(zone.GetUtcOffset(DateTimeOffset.UtcNow), Is.EqualTo(TimeSpan.FromHours(8)));
        Assert.That(_log.Warnings, Has.Count.EqualTo(1));
    }

    [Test]
    public void Pick_WhenEvenTheDefaultZoneIsMissing_IsUtcNotAnException()
    {
        // Arrange：精簡的映像檔可能沒有時區資料，連預設的 Asia/Taipei 都找不到。
        static TimeZoneInfo? Nothing(string _) => null;

        // Act
        var zone = OrganizationClock.Pick("Asia/Taipei", Nothing);

        // Assert
        Assert.That(zone, Is.EqualTo(TimeZoneInfo.Utc));
    }

    [Test]
    public async Task TimeZoneAsync_UnknownOrganization_IsTheDefaultWithoutAWarning()
    {
        // Act
        var zone = await Clock.TimeZoneAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.That(zone.GetUtcOffset(DateTimeOffset.UtcNow), Is.EqualTo(TimeSpan.FromHours(8)));
        Assert.That(_log.Warnings, Is.Empty);
    }

    [Test]
    public async Task TimeZoneAsync_ChangedBehindTheCache_IsReadAgainOnlyAfterFiveMinutes()
    {
        // Arrange
        var owner = await _host.StartWithOwnerAsync();
        var first = await Clock.TimeZoneAsync(owner.OrganizationId, CancellationToken.None);
        await SetZoneAsync(owner.OrganizationId, Tokyo);

        // Act
        _time.Advance(TimeSpan.FromMinutes(4));
        var cached = await Clock.TimeZoneAsync(owner.OrganizationId, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(2));
        var refreshed = await Clock.TimeZoneAsync(owner.OrganizationId, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.GetUtcOffset(DateTimeOffset.UtcNow), Is.EqualTo(TimeSpan.FromHours(8)));
            Assert.That(cached.Id, Is.EqualTo(first.Id));
            Assert.That(refreshed.Id, Is.EqualTo(TimeZoneInfo.FindSystemTimeZoneById(Tokyo).Id));
        }
    }

    private async Task SetZoneAsync(Guid organizationId, string zone)
    {
        await using var db = await _host.CreateDbContextAsync();
        await db.Organizations
            .Where(o => o.Id == organizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.TimeZone, zone));
    }

    private sealed class WarningLog : ILogger<OrganizationClock>
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings => _warnings;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                _warnings.Add(formatter(state, exception));
            }
        }
    }
}
