using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.Tests;

/// <summary>第一個擁有者：只在完全沒有帳號的資料庫建立，所以每個測試都用新的資料庫。</summary>
[TestFixture]
public sealed class IdentityBootstrapperTests
{
    private RecordingPasswordHasher _hasher = null!;
    private IdentityTestHost _host = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _hasher = new RecordingPasswordHasher();
        _host = await IdentityTestHost.StartAsync(services => services.AddSingleton<IPasswordHasher<PasswordCredential>>(_hasher));
    }

    [TearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task EnsureFirstOwnerAsync_EmptyDatabase_CreatesOwnerWhoCanSignIn()
    {
        // Act
        var created = await _host.Bootstrapper.EnsureFirstOwnerAsync(TestData.Owner, CancellationToken.None);
        var user = await _host.Authentication.SignInAsync(TestData.Owner.Email, TestData.Owner.Password, CancellationToken.None);

        // Assert
        Assert.That(created, Is.True);
        Assert.That(user, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(user!.OrganizationName, Is.EqualTo(TestData.Owner.OrganizationName));
            Assert.That(user.DisplayName, Is.EqualTo(TestData.Owner.DisplayName));
            Assert.That(user.Email, Is.EqualTo(TestData.Owner.Email));
            Assert.That(user.Role, Is.EqualTo(OrganizationRole.Owner));
            Assert.That(user.IsOrganizationAdmin, Is.True);
        }
    }

    [Test]
    public async Task EnsureFirstOwnerAsync_AccountsAlreadyExist_DoesNothing()
    {
        // Arrange
        await _host.Bootstrapper.EnsureFirstOwnerAsync(TestData.Owner, CancellationToken.None);
        var another = TestData.Owner with { Email = "another-owner@example.com" };
        var producedBefore = _hasher.ProducedHashes.Count;

        // Act
        var created = await _host.Bootstrapper.EnsureFirstOwnerAsync(another, CancellationToken.None);

        // Assert：用不到的密碼不必算雜湊，每次啟動才不會白花這段時間
        Assert.That(created, Is.False);
        Assert.That(_hasher.ProducedHashes, Has.Count.EqualTo(producedBefore));
        Assert.That(await _host.Authentication.SignInAsync(another.Email, another.Password, CancellationToken.None), Is.Null);
    }

    [TestCaseSource(nameof(InvalidOwners))]
    public async Task EnsureFirstOwnerAsync_AccountsAlreadyExist_IgnoresInvalidSettings(FirstOwner owner)
    {
        // Arrange：第一次啟動已經建好擁有者，之後設定被改壞了（例如密碼改短）
        await _host.Bootstrapper.EnsureFirstOwnerAsync(TestData.Owner, CancellationToken.None);

        // Act：設定只在沒有帳號時用得到；這時候丟例外，容器就會一直重啟
        var created = await _host.Bootstrapper.EnsureFirstOwnerAsync(owner, CancellationToken.None);

        // Assert
        Assert.That(created, Is.False);
        Assert.That(await _host.Authentication.SignInAsync(TestData.Owner.Email, TestData.Owner.Password, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task EnsureFirstOwnerAsync_DecomposedEmail_OwnerCanSignInWithTheComposedForm()
    {
        // Arrange
        var (composed, decomposed) = TestData.UniqueAccentedEmail();

        // Act
        await _host.Bootstrapper.EnsureFirstOwnerAsync(TestData.Owner with { Email = decomposed }, CancellationToken.None);
        var user = await _host.Authentication.SignInAsync(composed, TestData.Owner.Password, CancellationToken.None);

        // Assert
        Assert.That(user?.Email, Is.EqualTo(composed));
    }

    [Test]
    public async Task EnsureFirstOwnerAsync_TwoProcessesStartTogether_CreatesOnlyOneOwner()
    {
        // Arrange：兩個程序接上同一個資料庫，而且設定的 Email 不一樣，光靠 Email 的唯一限制擋不住
        await using var otherProcess = await IdentityTestHost.StartAsync(_host.ConnectionString, configure: null);
        var owners = Enumerable.Range(0, 6)
            .Select(i => TestData.Owner with { Email = $"owner-{i}@example.com" })
            .ToList();
        var hosts = new[] { _host, otherProcess };

        // Act
        var results = await Task.WhenAll(owners.Select((owner, i) =>
            Task.Run(() => hosts[i % hosts.Length].Bootstrapper.EnsureFirstOwnerAsync(owner, CancellationToken.None))));
        var signedIn = await Task.WhenAll(owners.Select(owner =>
            _host.Authentication.SignInAsync(owner.Email, owner.Password, CancellationToken.None)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(created => created), Is.EqualTo(1));
            Assert.That(signedIn.Count(user => user is not null), Is.EqualTo(1));
        }
    }

    [TestCaseSource(nameof(InvalidOwners))]
    public void EnsureFirstOwnerAsync_InvalidSettings_ThrowsArgumentException(FirstOwner owner)
    {
        // Act & Assert：部署設定寫錯了，要讓啟動直接失敗
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _host.Bootstrapper.EnsureFirstOwnerAsync(owner, CancellationToken.None));

        Assert.That(exception!.Message, Does.Not.Contain(owner.Password).And.Not.Contain(owner.Email.Trim()), "錯誤訊息不能帶出密碼或 Email");
    }

    [Test]
    public async Task EnsureFirstOwnerAsync_InvalidSettings_CreatesNothing()
    {
        // Arrange
        var tooShort = TestData.Owner with { Password = "short" };
        Assert.ThrowsAsync<ArgumentException>(() => _host.Bootstrapper.EnsureFirstOwnerAsync(tooShort, CancellationToken.None));

        // Act：設定改對之後重新啟動
        var created = await _host.Bootstrapper.EnsureFirstOwnerAsync(TestData.Owner, CancellationToken.None);

        // Assert
        Assert.That(created, Is.True);
    }

    private static IEnumerable<TestCaseData> InvalidOwners()
    {
        var owner = TestData.Owner;
        yield return new TestCaseData(owner with { OrganizationName = "  " }).SetArgDisplayNames("組織名稱空白");
        yield return new TestCaseData(owner with { OrganizationName = new string('組', IdentityDbContext.MaxOrganizationNameLength + 1) })
            .SetArgDisplayNames("組織名稱太長");
        yield return new TestCaseData(owner with { OrganizationName = "範例" + (char)0x0000 + "組織" }).SetArgDisplayNames("組織名稱有 NUL");
        yield return new TestCaseData(owner with { OrganizationName = "範例" + (char)0x000A + "組織" }).SetArgDisplayNames("組織名稱中間有換行");
        yield return new TestCaseData(owner with { OrganizationName = "範例" + (char)0xD800 + "組織" }).SetArgDisplayNames("組織名稱有落單的代理字元");
        yield return new TestCaseData(owner with { DisplayName = "" }).SetArgDisplayNames("顯示名稱空白");
        yield return new TestCaseData(owner with { DisplayName = new string('名', IdentityRules.MaxDisplayNameLength + 1) })
            .SetArgDisplayNames("顯示名稱太長");
        yield return new TestCaseData(owner with { DisplayName = "範例" + (char)0x0000 + "擁有者" }).SetArgDisplayNames("顯示名稱有 NUL");
        yield return new TestCaseData(owner with { Email = "not-an-email" }).SetArgDisplayNames("Email 格式不對");
        yield return new TestCaseData(owner with { Email = "owner" + (char)0x0000 + "@example.com" }).SetArgDisplayNames("Email 有 NUL");
        yield return new TestCaseData(owner with { Email = "owner" + (char)0xD800 + "@example.com" }).SetArgDisplayNames("Email 有落單的代理字元");
        yield return new TestCaseData(owner with { Password = new string('p', IdentityRules.MinPasswordLength - 1) })
            .SetArgDisplayNames("密碼太短");
        yield return new TestCaseData(owner with { Password = new string('p', IdentityRules.MaxPasswordLength + 1) })
            .SetArgDisplayNames("密碼太長");
    }
}
