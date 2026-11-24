using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace PmTool.Identity.Tests;

/// <summary>登入。失敗的情況呼叫端分不出是哪一種，所以只回 null。</summary>
[TestFixture]
public sealed class AuthenticationServiceTests
{
    private readonly RecordingPasswordHasher _hasher = new();
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await IdentityTestHost.StartAsync(services => services.AddSingleton<IPasswordHasher<PasswordCredential>>(_hasher));
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task SignInAsync_WrongPassword_ReturnsNull()
    {
        // Act
        var user = await _host.Authentication.SignInAsync(TestData.Owner.Email, "wrong-password", CancellationToken.None);

        // Assert
        Assert.That(user, Is.Null);
    }

    [Test]
    public async Task SignInAsync_UnknownEmail_ReturnsNullAfterHashingWithTheSameParameters()
    {
        // Arrange
        var verifiedBefore = _hasher.VerifiedHashes.Count;
        await _host.Authentication.SignInAsync(TestData.Owner.Email, TestData.Password, CancellationToken.None);
        var ownerHash = _hasher.VerifiedHashes[^1];

        // Act
        var user = await _host.Authentication.SignInAsync("nobody@example.com", TestData.Password, CancellationToken.None);

        // Assert：沒有這個 Email 也要算一次雜湊，而且參數一樣，花的時間才會一樣
        var verified = _hasher.VerifiedHashes;
        Assert.That(user, Is.Null);
        Assert.That(verified, Has.Count.EqualTo(verifiedBefore + 2));
        Assert.That(HashParameters(verified[^1]), Is.EqualTo(HashParameters(ownerHash)));
    }

    [TestCaseSource(typeof(TestData), nameof(TestData.ImpossibleEmails))]
    public async Task SignInAsync_EmailThatCannotBeValid_ReturnsNullAfterHashingWithTheSameParameters(string email)
    {
        // Arrange
        var verifiedBefore = _hasher.VerifiedHashes.Count;
        await _host.Authentication.SignInAsync(TestData.Owner.Email, TestData.Password, CancellationToken.None);
        var ownerHash = _hasher.VerifiedHashes[^1];

        // Act：NUL 這類字元送進 PostgreSQL 會丟例外，對外就變成 500
        var user = await _host.Authentication.SignInAsync(email, TestData.Password, CancellationToken.None);

        // Assert
        var verified = _hasher.VerifiedHashes;
        Assert.That(user, Is.Null);
        Assert.That(verified, Has.Count.EqualTo(verifiedBefore + 2));
        Assert.That(HashParameters(verified[^1]), Is.EqualTo(HashParameters(ownerHash)));
    }

    [TestCaseSource(typeof(TestData), nameof(TestData.ImpossibleEmails))]
    public async Task SignInAsync_EmailThatCannotBeValid_DoesNotQueryTheDatabase(string email)
    {
        // Arrange：資料庫連不上，一查就會丟例外
        var hasher = new RecordingPasswordHasher();
        await using var host = IdentityTestHost.StartWithoutDatabase(services => services.AddSingleton<IPasswordHasher<PasswordCredential>>(hasher));

        // Act
        var user = await host.Authentication.SignInAsync(email, TestData.Password, CancellationToken.None);

        // Assert：沒查資料庫也照樣比對一次雜湊，時間才和查無此 Email 一樣
        Assert.That(user, Is.Null);
        Assert.That(hasher.VerifiedHashes, Has.Count.EqualTo(1));
    }

    [TestCase("OWNER@EXAMPLE.COM")]
    [TestCase("  owner@example.com\t")]
    [TestCase(" Owner@Example.Com ")]
    public async Task SignInAsync_EmailInAnotherCaseOrWithSpaces_ReturnsUser(string email)
    {
        // Act
        var user = await _host.Authentication.SignInAsync(email, TestData.Password, CancellationToken.None);

        // Assert
        Assert.That(user, Is.EqualTo(_owner));
    }

    [TestCaseSource(nameof(UnicodeForms))]
    public async Task SignInAsync_EmailInTheOtherUnicodeForm_ReturnsUser(bool createWithComposed)
    {
        // Arrange：兩種寫法畫面上一模一樣，使用者分不出自己打的是哪一種
        var (composed, decomposed) = TestData.UniqueAccentedEmail();
        var (createWith, signInWith) = createWithComposed ? (composed, decomposed) : (decomposed, composed);
        var person = new NewPerson("範例使用者", createWith, TestData.Password, OrganizationRole.Member);
        var created = await _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None);

        // Act
        var user = await _host.Authentication.SignInAsync(signInWith, TestData.Password, CancellationToken.None);

        // Assert
        Assert.That(user?.PersonId, Is.EqualTo(created.Value.PersonId));
        Assert.That(user?.Email, Is.EqualTo(composed));
    }

    [Test]
    public async Task SignInAsync_PasswordLongerThanLimit_ReturnsNullWithoutHashing()
    {
        // Arrange
        var verifiedBefore = _hasher.VerifiedHashes.Count;
        var producedBefore = _hasher.ProducedHashes.Count;
        var password = new string('p', IdentityRules.MaxPasswordLength + 1);

        // Act
        var user = await _host.Authentication.SignInAsync(TestData.Owner.Email, password, CancellationToken.None);

        // Assert
        Assert.That(user, Is.Null);
        Assert.That(_hasher.VerifiedHashes, Has.Count.EqualTo(verifiedBefore));
        Assert.That(_hasher.ProducedHashes, Has.Count.EqualTo(producedBefore));
    }

    [Test]
    public async Task SignInAsync_StoredHash_IsNotThePlainPassword()
    {
        // Act
        await _host.Authentication.SignInAsync(TestData.Owner.Email, TestData.Password, CancellationToken.None);

        // Assert
        var storedHash = _hasher.VerifiedHashes[^1];
        Assert.That(storedHash, Does.Not.Contain(TestData.Password));
        Assert.That(new PasswordHasher<PasswordCredential>().VerifyHashedPassword(PasswordCredential.Instance, storedHash, TestData.Password),
            Is.EqualTo(PasswordVerificationResult.Success));
    }

    [Test]
    public async Task SignInAsync_HashMadeWithWeakerSettings_IsReplacedOnSuccessfulSignIn()
    {
        // Arrange：新版把雜湊的次數調高了，舊的雜湊在下次登入時要換成新的。會改到雜湊，所以另外開一個資料庫
        await using var original = await IdentityTestHost.StartAsync();
        var owner = await original.StartWithOwnerAsync();
        var stronger = new RecordingPasswordHasher(new PasswordHasherOptions { IterationCount = new PasswordHasherOptions().IterationCount * 2 });
        await using var upgraded = await IdentityTestHost.StartAsync(
            original.ConnectionString, services => services.AddSingleton<IPasswordHasher<PasswordCredential>>(stronger));

        // Act
        var first = await upgraded.Authentication.SignInAsync(TestData.Owner.Email, TestData.Password, CancellationToken.None);
        var second = await upgraded.Authentication.SignInAsync(TestData.Owner.Email, TestData.Password, CancellationToken.None);

        // Assert
        Assert.That(first, Is.EqualTo(owner));
        Assert.That(second, Is.EqualTo(owner));
        Assert.That(stronger.ProducedHashes, Has.Count.EqualTo(1), "只在第一次登入時重算");
        Assert.That(stronger.VerifiedHashes[1], Is.EqualTo(stronger.ProducedHashes[0]), "第二次登入比對的是重算後存進去的雜湊");
    }

    [Test]
    public async Task DisabledAccount_CannotSignIn()
    {
        // Arrange：停用會影響同一個資料庫裡的其他測試，所以另外開一個
        var hasher = new RecordingPasswordHasher();
        await using var host = await IdentityTestHost.StartAsync(services => services.AddSingleton<IPasswordHasher<PasswordCredential>>(hasher));
        var owner = await host.StartWithOwnerAsync();
        await host.DisableAccountAsync(owner.AccountId);
        var verifiedBefore = hasher.VerifiedHashes.Count;

        // Act
        var signedIn = await host.Authentication.SignInAsync(TestData.Owner.Email, TestData.Password, CancellationToken.None);

        // Assert：停用的帳號也照樣比對一次密碼，時間才和其他失敗一樣
        Assert.That(signedIn, Is.Null);
        Assert.That(hasher.VerifiedHashes, Has.Count.EqualTo(verifiedBefore + 1));
    }

    private static IEnumerable<TestCaseData> UnicodeForms()
    {
        yield return new TestCaseData(true).SetArgDisplayNames("建立用組合字，登入用分解字");
        yield return new TestCaseData(false).SetArgDisplayNames("建立用分解字，登入用組合字");
    }

    /// <summary>雜湊的版本、演算法、次數與鹽的長度都寫在開頭；這幾個位元組一樣，算的時間就一樣。</summary>
    private static string HashParameters(string hash) => Convert.ToHexString(Convert.FromBase64String(hash), 0, 13);
}
