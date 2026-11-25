using PmTool.Api.Tests.Support;
using PmTool.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace PmTool.Api.Tests;

/// <summary>儲存空間上限的設定：沒設用預設，0 是不限制，設錯就讓啟動失敗並說出是哪個設定。</summary>
[TestFixture]
public sealed class StorageQuotaConfigurationTests
{
    private static ApiFactory Factory(params (string Key, string Value)[] settings) =>
        new(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { Settings = settings.ToDictionary(s => s.Key, s => (string?)s.Value) });

    [Test]
    public void Quota_NothingConfigured_UsesTheDefaults()
    {
        // Arrange
        using var factory = Factory();

        // Act
        var quota = factory.Services.GetRequiredService<StorageQuotaSettings>();

        // Assert
        Assert.That(quota, Is.EqualTo(StorageQuotaSettings.Default));
    }

    [Test]
    public void Quota_ValuesInGiB_AreConvertedToBytes()
    {
        // Arrange
        using var factory = Factory(
            (StorageQuotaSettings.OrganizationGiBSetting, "50"), (StorageQuotaSettings.PersonDailyGiBSetting, " 5 "));

        // Act
        var quota = factory.Services.GetRequiredService<StorageQuotaSettings>();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(quota.OrganizationBytes, Is.EqualTo(50 * StorageQuotaSettings.GiB));
            Assert.That(quota.PersonBytesPerDay, Is.EqualTo(5 * StorageQuotaSettings.GiB));
        }
    }

    [Test]
    public void Quota_Zero_MeansNoLimit()
    {
        // Arrange
        using var factory = Factory(
            (StorageQuotaSettings.OrganizationGiBSetting, "0"), (StorageQuotaSettings.PersonDailyGiBSetting, "0"));

        // Act
        var quota = factory.Services.GetRequiredService<StorageQuotaSettings>();

        // Assert
        Assert.That(quota, Is.EqualTo(new StorageQuotaSettings(0, 0)));
    }

    [TestCase(StorageQuotaSettings.OrganizationGiBSetting, "abc")]
    [TestCase(StorageQuotaSettings.OrganizationGiBSetting, "-1")]
    [TestCase(StorageQuotaSettings.PersonDailyGiBSetting, "1.5")]
    [TestCase(StorageQuotaSettings.PersonDailyGiBSetting, "99999999999999999999")]
    [TestCase(StorageQuotaSettings.PersonDailyGiBSetting, "9223372036854775807")]
    public void Quota_NotAWholeNonNegativeNumber_FailsStartupAndNamesTheSetting(string key, string value)
    {
        // Arrange
        using var factory = Factory((key, value));

        // Act
        var failure = Assert.Catch<InvalidOperationException>(() => _ = factory.Services);

        // Assert
        Assert.That(failure?.Message, Does.Contain(key));
    }
}
