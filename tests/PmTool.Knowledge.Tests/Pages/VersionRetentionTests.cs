using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Knowledge.Pages;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>
/// 版本超過上限時先刪最舊的中間版本：第 1 版、最新的一版、組織時區每一天最早與最晚的一版一直留著。
/// 兩個帳號輪流存檔，只洗得掉當天的中間版本，以前每一天開頭與結尾的樣子都在。
/// </summary>
[TestFixture]
public sealed class VersionRetentionTests
{
    private static readonly TimeZoneInfo Taipei = TimeZoneInfo.FindSystemTimeZoneById(OrganizationTime.DefaultTimeZoneId);

    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private static PageVersions.VersionStamp Stamp(int number, DateTimeOffset at) => new(Guid.NewGuid(), number, at);

    [Test]
    public void Trim_DeletesTheOldestVersionsThatAreNotProtected()
    {
        // Arrange：同一天裡的 5 版，只有第 1 版是當天最早的。
        var day = new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);
        var versions = Enumerable.Range(1, 5).Select(n => Stamp(n, day.AddMinutes(n))).ToList();

        // Act
        var doomed = PageVersions.Trim(versions, Taipei, surplus: 2);

        // Assert
        Assert.That(doomed, Is.EqualTo(new[] { versions[1].Id, versions[2].Id }));
    }

    [Test]
    public void Trim_KeepsTheFirstAndLastVersionOfEveryDayInTheOrganizationsTimeZone()
    {
        // Arrange：UTC 的 16:30 是台北隔天的 00:30，所以它是 11/3 的第一版；11/2 的最後一版是第 2 版。
        var versions = new List<PageVersions.VersionStamp>
        {
            Stamp(1, new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero)),
            Stamp(2, new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero)),
            Stamp(3, new DateTimeOffset(2026, 11, 2, 16, 30, 0, TimeSpan.Zero)),
            Stamp(4, new DateTimeOffset(2026, 11, 2, 17, 0, 0, TimeSpan.Zero)),
            Stamp(5, new DateTimeOffset(2026, 11, 2, 18, 0, 0, TimeSpan.Zero)),
        };

        // Act
        var doomed = PageVersions.Trim(versions, Taipei, surplus: 1);

        // Assert
        Assert.That(doomed, Is.EqualTo(new[] { versions[3].Id }));
    }

    [Test]
    public void Trim_KeepsTheLastVersionOfEachEarlierDayWhileMiddleVersionsGoFirst()
    {
        // Arrange：第一天 3 版、第二天 4 版（台北時間）。
        var day1 = new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);
        var day2 = day1.AddDays(1);
        var versions = new[] { day1, day1.AddHours(1), day1.AddHours(2), day2, day2.AddHours(1), day2.AddHours(2), day2.AddHours(3) }
            .Select((at, index) => Stamp(index + 1, at))
            .ToList();

        // Act
        var doomed = PageVersions.Trim(versions, Taipei, surplus: 2);

        // Assert：保護的是第 1、3、4、7 版；先刪最舊的中間版本。
        Assert.That(doomed, Is.EqualTo(new[] { versions[1].Id, versions[4].Id }));
    }

    [Test]
    public void Trim_WhenEveryVersionIsAnEdgeOfADay_DeletesDayStartsOldestFirstThenDayEnds()
    {
        // Arrange：三天，每天 3 版。保護的邊界是每天的第 1、3 版，加上第 1 版與最新的一版。
        var start = new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);
        var versions = Enumerable.Range(0, 9).Select(index => Stamp(index + 1, start.AddDays(index / 3).AddHours(index % 3))).ToList();

        // Act
        var middlesAndDayStarts = PageVersions.Trim(versions, Taipei, surplus: 5);
        var everythingOptional = PageVersions.Trim(versions, Taipei, surplus: 6);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(middlesAndDayStarts, Is.EqualTo(new[] { versions[1].Id, versions[4].Id, versions[7].Id, versions[3].Id, versions[6].Id }));
            Assert.That(everythingOptional, Is.EqualTo(new[] { versions[1].Id, versions[4].Id, versions[7].Id, versions[3].Id, versions[6].Id, versions[2].Id }));
        }
    }

    [Test]
    public void Trim_WhenOnlyProtectedVersionsAreLeft_DeletesTheOldestDayStartsButNeverTheFirstOrTheLatest()
    {
        // Arrange：每一版各在不同的一天，都是「每天的第一版」。
        var start = new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);
        var versions = Enumerable.Range(1, 5).Select(n => Stamp(n, start.AddDays(n))).ToList();

        // Act
        var doomed = PageVersions.Trim(versions, Taipei, surplus: 3);

        // Assert
        Assert.That(doomed, Is.EqualTo(new[] { versions[1].Id, versions[2].Id, versions[3].Id }));
    }

    [Test]
    public async Task UpdateAsync_MoreThanTheMaximumVersions_KeepsTheFirstVersionAndEachDaysFirstAndLastVersion()
    {
        // Arrange：第一天 4 版，隔天又有很多版（兩個人輪流存，每次另開一版；第一次存檔的人要和建立的人不同，才不會併進第 1 版）。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "0");
        var people = new[] { org.Admin, org.Member() };
        var revision = page.Revision;
        for (var index = 1; index <= 3; index++)
        {
            revision = await SaveAsync(people[index % 2], page.Number, $"day1-{index}", revision);
        }

        _host.Clock.Advance(TimeSpan.FromDays(1));
        for (var index = 1; index <= KnowledgeRules.MaxVersionsPerPage + 5; index++)
        {
            revision = await SaveAsync(people[index % 2], page.Number, $"day2-{index}", revision);
        }

        // Act
        var detail = await _host.Pages.GetAsync(org.Admin, "HR", page.Number, CancellationToken.None);
        await using var db = await _host.CreateDbContextAsync();
        var kept = await db.PageVersions.AsNoTracking().Where(v => v.PageId == page.Id).OrderBy(v => v.Number).Select(v => new { v.Number, v.Body }).ToListAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detail.Value.VersionCount, Is.EqualTo(KnowledgeRules.MaxVersionsPerPage));
            Assert.That(kept[0].Body, Is.EqualTo("0"), "第 1 版");
            Assert.That(kept.Any(v => v.Body == "day2-1"), Is.True, "第二天的第一版");
            Assert.That(kept[^1].Body, Is.EqualTo($"day2-{KnowledgeRules.MaxVersionsPerPage + 5}"), "最新的一版");
            Assert.That(kept.Any(v => v.Body == "day1-3"), Is.True, "第一天的最後一版在大量存檔之後還在");
            Assert.That(kept.Any(v => v.Body == "day1-1"), Is.False, "第一天的中間版本先被洗掉");
        }
    }

    private async Task<long> SaveAsync(Actor actor, int pageNumber, string body, long expectedRevision)
    {
        var saved = await _host.Pages.UpdateAsync(actor, "HR", pageNumber, new PageChanges(null, body, expectedRevision), CancellationToken.None);
        Assert.That(saved.Succeeded, Is.True, () => saved.Failure?.Message ?? string.Empty);
        return saved.Value.Revision;
    }
}
