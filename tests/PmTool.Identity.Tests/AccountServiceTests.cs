using System.Buffers.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>改自己的密碼：先確認目前的密碼，成功後其他工作階段全部登出，這一個留著。</summary>
[TestFixture]
public sealed class AccountServiceTests
{
    private const string NewPassword = "brand-new-password";

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
    public async Task ChangeOwnPassword_Accepted_SignsOutTheOtherSessionsAndKeepsThisOne()
    {
        // Arrange：另外兩個工作階段先查過一次，結果進了快取；改密碼要立刻讓它們失效。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var current = await _host.StartSessionAsync(user);
        var second = await _host.StartSessionAsync(user);
        var third = await _host.StartSessionAsync(user);
        await _host.AreActiveAsync(second, third);

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(current.Token, TestData.Password, NewPassword, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
        Assert.That(result.Value, Is.EqualTo(new PasswordChanged(2)));
        Assert.That(await _host.AreActiveAsync(current, second, third), Is.EqualTo(new[] { true, false, false }));
    }

    [Test]
    public async Task ChangeOwnPassword_Accepted_TheNewPasswordSignsInAndTheOldOneDoesNot()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);

        // Act
        await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, NewPassword, CancellationToken.None);

        // Assert
        Assert.That(await _host.Authentication.SignInAsync(user.Email, NewPassword, CancellationToken.None), Is.EqualTo(user));
        Assert.That(await _host.Authentication.SignInAsync(user.Email, TestData.Password, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ChangeOwnPassword_SameAsTheCurrentPassword_IsAccepted()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, TestData.Password, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new PasswordChanged(0)));
        Assert.That(await _host.Authentication.SignInAsync(user.Email, TestData.Password, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task ChangeOwnPassword_WrongCurrentPassword_ChangesNothing()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var current = await _host.StartSessionAsync(user);
        var other = await _host.StartSessionAsync(user);

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(current.Token, "wrong-password", NewPassword, CancellationToken.None);

        // Assert
        AssertInvalid(result, (IdentityErrorCodes.PasswordIncorrect, "currentPassword"));
        Assert.That(await _host.AreActiveAsync(current, other), Is.All.True);
        Assert.That(await _host.Authentication.SignInAsync(user.Email, TestData.Password, CancellationToken.None), Is.Not.Null);
    }

    [TestCase(IdentityRules.MinPasswordLength - 1, IdentityErrorCodes.PasswordTooShort, TestName = "ChangeOwnPassword_NewPasswordTooShort_ReturnsTooShortOnNewPassword")]
    [TestCase(IdentityRules.MaxPasswordLength + 1, IdentityErrorCodes.PasswordTooLong, TestName = "ChangeOwnPassword_NewPasswordTooLong_ReturnsTooLongOnNewPassword")]
    public async Task ChangeOwnPassword_NewPasswordOfTheWrongLength(int length, string code)
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, new string('p', length), CancellationToken.None);

        // Assert
        AssertInvalid(result, (code, "newPassword"));
        Assert.That(await _host.Authentication.SignInAsync(user.Email, TestData.Password, CancellationToken.None), Is.Not.Null);
    }

    [TestCase(IdentityRules.MinPasswordLength)]
    [TestCase(IdentityRules.MaxPasswordLength)]
    public async Task ChangeOwnPassword_NewPasswordAtTheLimits_IsAccepted(int length)
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, new string('p', length), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [Test]
    public async Task ChangeOwnPassword_BothPasswordsWrong_ListsBothProblems()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, "wrong-password", "short", CancellationToken.None);

        // Assert：網頁一次把兩個欄位都標出來
        AssertInvalid(
            result, (IdentityErrorCodes.PasswordIncorrect, "currentPassword"), (IdentityErrorCodes.PasswordTooShort, "newPassword"));
    }

    [Test]
    public async Task ChangeOwnPassword_CurrentPasswordLongerThanTheLimit_IsIncorrectWithoutHashing()
    {
        // Arrange：存著的密碼不可能那麼長；拿去算雜湊只會讓人用很長的字串拖垮伺服器。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var verifiedBefore = _hasher.VerifiedHashes.Count;

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(
            session.Token, new string('p', IdentityRules.MaxPasswordLength + 1), NewPassword, CancellationToken.None);

        // Assert
        AssertInvalid(result, (IdentityErrorCodes.PasswordIncorrect, "currentPassword"));
        Assert.That(_hasher.VerifiedHashes, Has.Count.EqualTo(verifiedBefore));
    }

    [TestCase("malformed", TestName = "ChangeOwnPassword_MalformedToken_ReturnsForbidden")]
    [TestCase("unknown", TestName = "ChangeOwnPassword_UnknownToken_ReturnsForbidden")]
    [TestCase("ended", TestName = "ChangeOwnPassword_EndedSession_ReturnsForbidden")]
    public async Task ChangeOwnPassword_TokenThatIsNotAnActiveSession(string kind)
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var token = kind switch
        {
            "malformed" => "not-a-token",
            "unknown" => Base64Url.EncodeToString(new byte[32]),
            _ => await StartAndEndAsync(user),
        };

        // Act
        var result = await _host.Accounts.ChangeOwnPasswordAsync(token, TestData.Password, NewPassword, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
        Assert.That(await _host.Authentication.SignInAsync(user.Email, TestData.Password, CancellationToken.None), Is.Not.Null);
    }

    private async Task<string> StartAndEndAsync(AuthenticatedUser user)
    {
        var session = await _host.StartSessionAsync(user);
        await _host.Sessions.EndAsync(session.Token, CancellationToken.None);
        return session.Token;
    }

    private static void AssertInvalid(Result<PasswordChanged> result, params (string Code, string Path)[] expected)
    {
        Assert.That(result.Failure, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure.Code, Is.EqualTo(expected[0].Code));
            Assert.That(result.Failure.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(expected));
            Assert.That(result.Failure.Details.Select(d => d.Message), Has.All.Not.Empty);
        }
    }
}
