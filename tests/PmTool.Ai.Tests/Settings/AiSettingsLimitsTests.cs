using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Tests.Settings;

/// <summary>組織連線的用量上限在設定裡的讀寫與驗證，以及這個月的用量（AI-01）。</summary>
[TestFixture]
public sealed class AiSettingsLimitsTests
{
    [Test]
    public async Task Get_NeverConfigured_ReportsTheDefaultLimitsAndEmptyUsage()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();

        // Act
        var settings = (await host.Get<IAiSettingsService>().GetAsync(AiTestData.Admin, CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.Limits, Is.EqualTo(AiUsageLimits.Default));
            Assert.That(settings.MonthUsage, Is.EqualTo(new AiMonthUsage("2026-11", 0, 0)));
        }
    }

    [Test]
    public async Task Save_WithLimits_StoresThemAndSaveWithoutLimitsKeepsThem()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();
        var service = host.Get<IAiSettingsService>();
        var limits = new AiUsageLimits(50, 1000, 2_000_000);

        // Act
        var saved = await service.SaveAsync(AiTestData.Admin, new AiSettingsInput(false, null) { Limits = limits }, CancellationToken.None);
        var again = await service.SaveAsync(AiTestData.Admin, new AiSettingsInput(false, null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Value.Limits, Is.EqualTo(limits));
            Assert.That(again.Value.Limits, Is.EqualTo(limits));
        }
    }

    [Test]
    public async Task Save_AllNull_MeansNoLimit()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();

        // Act
        var saved = await host.Get<IAiSettingsService>().SaveAsync(
            AiTestData.Admin, new AiSettingsInput(false, null) { Limits = new AiUsageLimits(null, null, null) }, CancellationToken.None);

        // Assert
        Assert.That(saved.Value.Limits, Is.EqualTo(new AiUsageLimits(null, null, null)));
    }

    [TestCase(0, null, null, "limits.dailyRequestsPerPerson")]
    [TestCase(AiRules.MaxHostRequestLimit + 1, null, null, "limits.dailyRequestsPerPerson")]
    [TestCase(null, -1, null, "limits.monthlyRequests")]
    [TestCase(null, null, 0L, "limits.monthlyTokens")]
    [TestCase(null, null, AiRules.MaxHostTokenLimit + 1, "limits.monthlyTokens")]
    public async Task Save_OutOfRangeLimit_IsRejectedWithThePath(int? daily, int? monthly, long? tokens, string path)
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();

        // Act
        var saved = await host.Get<IAiSettingsService>().SaveAsync(
            AiTestData.Admin, new AiSettingsInput(false, null) { Limits = new AiUsageLimits(daily, monthly, tokens) }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
            Assert.That(saved.Failure?.Details.Select(d => (d.Code, d.Path)), Does.Contain((AiErrorCodes.FieldInvalid, path)));
        }
    }

    [Test]
    public async Task Get_MonthUsage_AddsUpThisMonthsCountedRowsOnly()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();
        var now = host.Get<TimeProvider>().GetUtcNow();
        await using (var db = await host.CreateDbContextAsync())
        {
            db.ConnectionUsage.AddRange(
                Usage("settled", now, 100, 20),
                Usage("reserved", now, 0, 0),
                Usage("released", now, 999, 999),
                Usage("settled", now.AddDays(-40), 500, 500));
            await db.SaveChangesAsync();
        }

        // Act
        var usage = (await host.Get<IAiSettingsService>().GetAsync(AiTestData.Admin, CancellationToken.None)).Value.MonthUsage;

        // Assert
        Assert.That(usage, Is.EqualTo(new AiMonthUsage("2026-11", 2, 120)));
    }

    private static AiConnectionUsageRow Usage(string state, DateTimeOffset at, long input, long output) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = AiTestData.OrganizationId,
        ConnectionId = Guid.CreateVersion7(),
        PersonId = AiTestData.Member.PersonId,
        ProjectKey = "WEB",
        State = state,
        InputTokens = input,
        OutputTokens = output,
        CreatedAt = at,
    };
}
