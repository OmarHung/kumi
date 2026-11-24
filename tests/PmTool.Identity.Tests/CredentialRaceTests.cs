using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace PmTool.Identity.Tests;

/// <summary>
/// 驗證密碼要花一點時間（PBKDF2），這段時間裡帳號可能被重設、停用或作廢工作階段。
/// 驗過的密碼必須綁在寫入的那一刻，否則拿舊密碼的人會在那些操作之後，還留著一個工作階段或蓋掉新的密碼。
/// </summary>
[TestFixture]
public sealed class CredentialRaceTests
{
    private const string ResetPassword = "reset-by-the-owner-0001";
    private const string AttackerPassword = "chosen-by-someone-else-0001";
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    [Test]
    public async Task SignIn_PasswordResetWhileTheOldPasswordIsBeingVerified_OpensNoSession()
    {
        // Arrange：有人拿外流的舊密碼登入，比對到一半時擁有者重設了密碼。
        using var gate = new GatedPasswordHasher();
        await using var host = await StartAsync(gate);
        var owner = await host.StartWithOwnerAsync();
        var user = await host.AddUserAsync(owner.ToActor());
        gate.Arm(TestData.Password);
        var signingIn = Task.Run(() => host.Sessions.SignInAsync(user.Email, TestData.Password, CancellationToken.None));
        await gate.Entered.WaitAsync(MaxWait);

        // Act
        var reset = await host.People.ResetPasswordAsync(owner.ToActor(), user.PersonId, ResetPassword, CancellationToken.None);
        gate.Release();
        var signedIn = await signingIn.WaitAsync(MaxWait);

        // Assert
        Assert.That(reset.Succeeded, Is.True, reset.Failure?.Message);
        Assert.That(signedIn, Is.Null);
        Assert.That(await host.Sessions.SignInAsync(user.Email, ResetPassword, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task SignIn_AccountDisabledWhileThePasswordIsBeingVerified_OpensNoSession()
    {
        // Arrange：停用時作廢的是「當下」所有的工作階段；比對中的登入如果之後才寫入，重新啟用時它就復活了。
        using var gate = new GatedPasswordHasher();
        await using var host = await StartAsync(gate);
        var owner = await host.StartWithOwnerAsync();
        var user = await host.AddUserAsync(owner.ToActor());
        gate.Arm(TestData.Password);
        var signingIn = Task.Run(() => host.Sessions.SignInAsync(user.Email, TestData.Password, CancellationToken.None));
        await gate.Entered.WaitAsync(MaxWait);

        // Act
        var disabled = await host.People.SetDisabledAsync(owner.ToActor(), user.PersonId, true, CancellationToken.None);
        gate.Release();
        var signedIn = await signingIn.WaitAsync(MaxWait);

        // Assert
        Assert.That(disabled.Succeeded, Is.True, disabled.Failure?.Message);
        Assert.That(signedIn, Is.Null);
    }

    [Test]
    public async Task ChangeOwnPassword_PasswordResetWhileTheCurrentPasswordIsBeingVerified_KeepsTheResetPassword()
    {
        // Arrange：拿著偷來的工作階段與舊密碼的人在改密碼，比對到一半時擁有者重設了密碼。
        // 不擋的話，他選的新密碼會蓋掉擁有者的重設，而且他之後還能用新密碼登入。
        using var gate = new GatedPasswordHasher();
        await using var host = await StartAsync(gate);
        var owner = await host.StartWithOwnerAsync();
        var user = await host.AddUserAsync(owner.ToActor());
        var session = await host.StartSessionAsync(user);
        gate.Arm(TestData.Password);
        var changing = Task.Run(() => host.Accounts.ChangeOwnPasswordAsync(
            session.Token, TestData.Password, AttackerPassword, CancellationToken.None));
        await gate.Entered.WaitAsync(MaxWait);

        // Act
        var reset = await host.People.ResetPasswordAsync(owner.ToActor(), user.PersonId, ResetPassword, CancellationToken.None);
        gate.Release();
        var changed = await changing.WaitAsync(MaxWait);

        // Assert
        Assert.That(reset.Succeeded, Is.True, reset.Failure?.Message);
        Assert.That(changed.Failure?.Code, Is.EqualTo(IdentityErrorCodes.SessionEnded));
        Assert.That(await host.Sessions.SignInAsync(user.Email, ResetPassword, CancellationToken.None), Is.Not.Null);
        Assert.That(await host.Sessions.SignInAsync(user.Email, AttackerPassword, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ChangeOwnPassword_SessionEndedElsewhereWhileTheCurrentPasswordIsBeingVerified_ChangesNothing()
    {
        // Arrange：本人在另一個裝置按了「登出其他裝置」，正好在這個工作階段改密碼的半路上。
        using var gate = new GatedPasswordHasher();
        await using var host = await StartAsync(gate);
        var owner = await host.StartWithOwnerAsync();
        var user = await host.AddUserAsync(owner.ToActor());
        var stolen = await host.StartSessionAsync(user);
        var mine = await host.StartSessionAsync(user);
        gate.Arm(TestData.Password);
        var changing = Task.Run(() => host.Accounts.ChangeOwnPasswordAsync(
            stolen.Token, TestData.Password, AttackerPassword, CancellationToken.None));
        await gate.Entered.WaitAsync(MaxWait);

        // Act
        var signedOut = await host.Sessions.EndOthersAsync(mine.Token, CancellationToken.None);
        gate.Release();
        var changed = await changing.WaitAsync(MaxWait);

        // Assert
        Assert.That(signedOut, Is.EqualTo(1));
        Assert.That(changed.Failure?.Code, Is.EqualTo(IdentityErrorCodes.SessionEnded));
        Assert.That(await host.Sessions.SignInAsync(user.Email, TestData.Password, CancellationToken.None), Is.Not.Null);
        Assert.That(await host.Sessions.SignInAsync(user.Email, AttackerPassword, CancellationToken.None), Is.Null);
    }

    private static Task<IdentityTestHost> StartAsync(GatedPasswordHasher gate) =>
        IdentityTestHost.StartAsync(services => services.AddSingleton<IPasswordHasher<PasswordCredential>>(gate));
}
