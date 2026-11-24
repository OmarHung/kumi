using PmTool.Abstractions;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>版本的寫入（交接 4.5）：同一個人 15 分鐘內連續存檔併成同一版，一頁最多 1,000 版。</summary>
[TestFixture]
public sealed class PageVersionTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_SameAuthorWithinFifteenMinutes_MergesIntoTheSameVersion()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "一");

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(10));
        var second = await SaveAsync(org.Admin, page.Number, "二", page.Revision);
        _host.Clock.Advance(TimeSpan.FromMinutes(14));
        var third = await SaveAsync(org.Admin, page.Number, "三", second.Revision);

        // Assert：第 3 次距離上一次存檔 14 分鐘（不是距離第一版），所以還是同一版。
        Assert.That((second.VersionCount, third.VersionCount, third.Revision), Is.EqualTo((1, 1, 3L)));
    }

    [Test]
    public async Task UpdateAsync_ExactlyFifteenMinutesLater_StillMerges()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "一");
        _host.Clock.Advance(KnowledgeRules.VersionMergeWindow - TimeSpan.FromMilliseconds(1));

        // Act
        var second = await SaveAsync(org.Admin, page.Number, "二", page.Revision);

        // Assert
        Assert.That(second.VersionCount, Is.EqualTo(1));
    }

    [Test]
    public async Task UpdateAsync_AfterTheWindowPasses_StartsANewVersion()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "一");
        _host.Clock.Advance(TimeSpan.FromMinutes(16));

        // Act
        var second = await SaveAsync(org.Admin, page.Number, "二", page.Revision);
        var third = await SaveAsync(org.Admin, page.Number, "三", second.Revision);

        // Assert：第 3 次和第 2 次同一個人、時間很近，併進第 2 版。
        Assert.That((second.VersionCount, third.VersionCount), Is.EqualTo((2, 2)));
    }

    [Test]
    public async Task UpdateAsync_AnotherAuthorWithinTheWindow_StartsANewVersion()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "一");

        // Act
        var byOther = await SaveAsync(org.Member(), page.Number, "二", page.Revision);
        var backToFirst = await SaveAsync(org.Admin, page.Number, "三", byOther.Revision);

        // Assert
        Assert.That((byOther.VersionCount, backToFirst.VersionCount), Is.EqualTo((2, 3)));
    }

    [Test]
    public async Task UpdateAsync_MoreThanTheMaximumVersions_KeepsOnlyTheNewest()
    {
        // Arrange：兩個人輪流存檔，每次都另開一版，不必等 15 分鐘。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "0");
        var people = new[] { org.Member(), org.Admin };
        var revision = page.Revision;
        PageDetail? last = null;

        // Act
        for (var index = 1; index <= KnowledgeRules.MaxVersionsPerPage + 5; index++)
        {
            last = await SaveAsync(people[index % 2], page.Number, index.ToString(), revision);
            revision = last.Revision;
        }

        // Assert
        Assert.That(last!.VersionCount, Is.EqualTo(KnowledgeRules.MaxVersionsPerPage));
    }

    [Test]
    public async Task UpdateAsync_ARejectedSave_DoesNotWriteAVersion()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題");
        _host.Clock.Advance(TimeSpan.FromHours(1));

        // Act
        await _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges(null, "不會存", 99), CancellationToken.None);
        var reread = await _host.Pages.GetAsync(org.Admin, "HR", page.Number, CancellationToken.None);

        // Assert
        Assert.That(reread.Value.VersionCount, Is.EqualTo(1));
    }

    private async Task<PageDetail> SaveAsync(Actor actor, int pageNumber, string body, long expectedRevision)
    {
        var saved = await _host.Pages.UpdateAsync(actor, "HR", pageNumber, new PageChanges(null, body, expectedRevision), CancellationToken.None);
        Assert.That(saved.Succeeded, Is.True, () => saved.Failure!.Message);
        return saved.Value;
    }
}
