using PmTool.Abstractions;

namespace PmTool.Notifications.Tests;

/// <summary>每個人自己要不要收 Email 通知。</summary>
[TestFixture]
public sealed class EmailPreferenceTests : EmailTestBase
{
    private Actor Person() => new(Organization, Guid.CreateVersion7(), false);

    [Test]
    public async Task Get_NeverChanged_DefaultsToOnAndTheOrganizationNotEnabled()
    {
        // Act
        var preference = await Host.EmailPreferences.GetAsync(Person(), CancellationToken.None);

        // Assert
        Assert.That(preference, Is.EqualTo(new EmailPreference(true, false)));
    }

    [Test]
    public async Task Set_Off_IsRememberedAndOnlyForThatPerson()
    {
        // Arrange
        var me = Person();
        var someoneElse = Person();

        // Act
        var set = await Host.EmailPreferences.SetAsync(me, false, CancellationToken.None);
        var mine = await Host.EmailPreferences.GetAsync(me, CancellationToken.None);
        var theirs = await Host.EmailPreferences.GetAsync(someoneElse, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(set.Enabled, Is.False);
            Assert.That(mine.Enabled, Is.False);
            Assert.That(theirs.Enabled, Is.True);
        }
    }

    [Test]
    public async Task Set_OffThenOn_ComesBackOn()
    {
        // Arrange
        var me = Person();
        await Host.EmailPreferences.SetAsync(me, false, CancellationToken.None);

        // Act
        await Host.EmailPreferences.SetAsync(me, true, CancellationToken.None);

        // Assert
        Assert.That((await Host.EmailPreferences.GetAsync(me, CancellationToken.None)).Enabled, Is.True);
    }

    [Test]
    public async Task Set_SameValueTwiceAtTheSameTime_DoesNotFail()
    {
        // Arrange
        var me = Person();

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Host.EmailPreferences.SetAsync(me, false, CancellationToken.None)));

        // Assert
        Assert.That(results.Select(r => r.Enabled), Has.All.False);
    }

    [Test]
    public async Task Get_OrganizationEnabled_IsReflected()
    {
        // Arrange
        await EnableEmailAsync();
        var me = Person();

        // Act
        var enabled = await Host.EmailPreferences.GetAsync(me, CancellationToken.None);
        await EnableEmailAsync(GmailInput(enabled: false));
        var disabled = await Host.EmailPreferences.SetAsync(me, true, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(enabled.OrganizationEnabled, Is.True);
            Assert.That(disabled.OrganizationEnabled, Is.False);
        }
    }
}
