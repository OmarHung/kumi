namespace PmTool.Storage.Tests;

[TestFixture]
public sealed class StorageQuotaMessagesTests
{
    private const long GiB = StorageQuotaSettings.GiB;

    [Test]
    public void OrganizationFull_ShowsUsedWithOneDecimalAndTheLimitAsAWholeNumber()
    {
        // Arrange
        var check = new StorageQuotaCheck(StorageQuotaVerdict.OrganizationFull, 20 * GiB + GiB / 10 + 1, 20 * GiB, 0, 0);

        // Act
        var message = StorageQuotaMessages.OrganizationFull(check);

        // Assert
        Assert.That(message, Is.EqualTo("組織的附件已經用了 20.1 GiB，上限是 20 GiB。請刪掉不用的附件，或請管理員調高上限。"));
    }

    [Test]
    public void PersonDailyFull_ShowsBothNumbersInGiB()
    {
        // Arrange
        var check = new StorageQuotaCheck(StorageQuotaVerdict.PersonDailyFull, 0, 0, 2 * GiB, 2 * GiB);

        // Act
        var message = StorageQuotaMessages.PersonDailyFull(check);

        // Assert
        Assert.That(message, Is.EqualTo("你在過去 24 小時已經上傳 2.0 GiB，上限是 2 GiB，請稍後再試。"));
    }
}
