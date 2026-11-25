using PmTool.Abstractions;

namespace PmTool.Storage.Tests;

public sealed class StorageQuotaTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeSource(StorageArea area, long bytes, long files, long personBytes) : IStorageUsageSource
    {
        public DateTimeOffset? LastSince { get; private set; }

        public int Calls { get; private set; }

        public StorageArea Area => area;

        public Task<StorageAreaUsage> UsageAsync(Guid organizationId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new StorageAreaUsage(area, bytes, files));
        }

        public Task<long> UploadedBytesSinceAsync(Guid organizationId, Guid personId, DateTimeOffset since, CancellationToken cancellationToken)
        {
            Calls++;
            LastSince = since;
            return Task.FromResult(personBytes);
        }
    }

    private static StorageQuota Create(StorageQuotaSettings settings, params IStorageUsageSource[] sources) =>
        new(sources, settings, new FixedTime(Now));

    [Test]
    public async Task Allows_an_upload_when_both_limits_have_room()
    {
        var quota = Create(new StorageQuotaSettings(100, 50),
            new FakeSource(StorageArea.ItemAttachments, 30, 3, 10),
            new FakeSource(StorageArea.KnowledgeAttachments, 20, 2, 5));

        var check = await quota.CheckAsync(Organization, Person, 10, CancellationToken.None);

        Assert.That(check.IsAllowed, Is.True);
        Assert.That(check.OrganizationUsedBytes, Is.EqualTo(50));
        Assert.That(check.PersonUsedBytes, Is.EqualTo(15));
    }

    [Test]
    public async Task Preflight_blocks_only_when_the_organization_is_already_full()
    {
        var full = Create(new StorageQuotaSettings(100, 0), new FakeSource(StorageArea.ItemAttachments, 100, 1, 0));
        var room = Create(new StorageQuotaSettings(100, 0), new FakeSource(StorageArea.ItemAttachments, 99, 1, 0));

        var blocked = await full.CheckAsync(Organization, Person, 0, CancellationToken.None);
        var allowed = await room.CheckAsync(Organization, Person, 0, CancellationToken.None);

        Assert.That(blocked.Verdict, Is.EqualTo(StorageQuotaVerdict.OrganizationFull));
        Assert.That(allowed.IsAllowed, Is.True);
    }

    [Test]
    public async Task Confirmation_blocks_when_the_incoming_file_would_cross_the_limit()
    {
        var quota = Create(new StorageQuotaSettings(100, 0), new FakeSource(StorageArea.ItemAttachments, 60, 1, 0));

        var exactly = await quota.CheckAsync(Organization, Person, 40, CancellationToken.None);
        var over = await quota.CheckAsync(Organization, Person, 41, CancellationToken.None);

        Assert.That(exactly.IsAllowed, Is.True);
        Assert.That(over.Verdict, Is.EqualTo(StorageQuotaVerdict.OrganizationFull));
    }

    [Test]
    public async Task Blocks_when_the_person_has_uploaded_too_much_in_the_last_day()
    {
        var source = new FakeSource(StorageArea.ItemAttachments, 10, 1, 45);
        var quota = Create(new StorageQuotaSettings(1000, 50), source);

        var check = await quota.CheckAsync(Organization, Person, 6, CancellationToken.None);

        Assert.That(check.Verdict, Is.EqualTo(StorageQuotaVerdict.PersonDailyFull));
        Assert.That(source.LastSince, Is.EqualTo(Now - TimeSpan.FromHours(24)));
    }

    [Test]
    public async Task Skips_the_daily_limit_when_there_is_no_person()
    {
        var quota = Create(new StorageQuotaSettings(1000, 50), new FakeSource(StorageArea.ItemAttachments, 10, 1, 500));

        var check = await quota.CheckAsync(Organization, null, 100, CancellationToken.None);

        Assert.That(check.IsAllowed, Is.True);
    }

    [Test]
    public async Task Zero_means_no_limit_and_nothing_is_queried()
    {
        var source = new FakeSource(StorageArea.ItemAttachments, 1_000_000, 1, 1_000_000);
        var quota = Create(new StorageQuotaSettings(0, 0), source);

        var check = await quota.CheckAsync(Organization, Person, 1_000_000, CancellationToken.None);

        Assert.That(check.IsAllowed, Is.True);
        Assert.That(source.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task Report_is_forbidden_for_people_who_are_not_organization_admins()
    {
        var quota = Create(StorageQuotaSettings.Default);

        var result = await quota.ReportAsync(new Actor(Organization, Person, false), CancellationToken.None);

        Assert.That(result.Failure?.Code, Is.EqualTo(StorageErrorCodes.Forbidden));
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
    }

    [Test]
    public async Task Report_lists_every_area_and_hides_limits_of_zero()
    {
        var quota = Create(new StorageQuotaSettings(100, 0),
            new FakeSource(StorageArea.ItemAttachments, 30, 3, 0));

        var result = await quota.ReportAsync(new Actor(Organization, Person, true), CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Value.OrganizationLimitBytes, Is.EqualTo(100));
        Assert.That(result.Value.PersonDailyLimitBytes, Is.Null);
        Assert.That(result.Value.Areas.Select(a => (a.Area, a.Bytes, a.Files)), Is.EqualTo(new[]
        {
            (StorageArea.ItemAttachments, 30L, 3L),
            (StorageArea.KnowledgeAttachments, 0L, 0L),
        }));
        Assert.That(result.Value.TotalBytes, Is.EqualTo(30));
    }

    [Test]
    public void Lock_name_is_the_same_for_every_module_of_an_organization()
    {
        Assert.That(StorageQuotaRules.LockNameOf(Organization), Is.EqualTo("storage_quota:" + Organization.ToString("D")));
    }
}
