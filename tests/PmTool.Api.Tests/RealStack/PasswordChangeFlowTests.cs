using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 改自己的密碼，從 HTTP 一路到資料庫：另一個登入中的瀏覽器下一個請求就被登出。
/// TestServer 沒有來源 IP，登入與改密碼共用同一份額度（每分鐘 <see cref="ApiLimits.SignInPermitsPerWindow"/> 次），這個類別要算著用。
/// </summary>
[TestFixture]
public sealed class PasswordChangeFlowTests : RealStackTestBase
{
    private const string NewPassword = "real-stack-changed-0000";

    private SignedInClient _owner = null!;

    [OneTimeSetUp]
    public async Task SignInOwnerAsync() => _owner = await SignInAsOwnerAsync();

    [OneTimeTearDown]
    public void SignOutOwner() => _owner.Dispose();

    [Test]
    public async Task ChangePassword_WithTwoSessions_TheOtherIsSignedOutAtOnceAndOnlyTheNewPasswordWorks()
    {
        // Arrange：同一個人在兩個瀏覽器登入。
        var email = RealStackSteps.UniqueEmail("password");
        await RealStackSteps.CreateMemberAsync(_owner.Client, "Password Example", email);
        using var first = await SignInAsAsync(email, RealStackSteps.MemberPassword);
        using var second = await SignInAsAsync(email, RealStackSteps.MemberPassword);
        // 先查一次，結果進了快取：作廢要連快取一起清，下一個請求才會失效。
        Assert.That(await RealStackSteps.StatusOfAsync(second.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));

        // Act
        using var response = await first.Client.PutAsync(
            "/v1/account/password", ApiRequests.Json(new { currentPassword = RealStackSteps.MemberPassword, newPassword = NewPassword }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using var anonymous = Factory.CreateApiClient();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["signedOutSessions"]?.GetValue<int>(), Is.EqualTo(1));
            Assert.That(await RealStackSteps.StatusOfAsync(second.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(await RealStackSteps.StatusOfAsync(first.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await RealStackSteps.TrySignInAsync(anonymous, email, RealStackSteps.MemberPassword), Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(await RealStackSteps.TrySignInAsync(anonymous, email, NewPassword), Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task ChangePassword_WrongCurrentPassword_Returns422AndKeepsEverySession()
    {
        // Arrange
        var email = RealStackSteps.UniqueEmail("wrong");
        await RealStackSteps.CreateMemberAsync(_owner.Client, "Wrong Example", email);
        using var member = await SignInAsAsync(email, RealStackSteps.MemberPassword);

        // Act
        using var response = await member.Client.PutAsync(
            "/v1/account/password", ApiRequests.Json(new { currentPassword = "not-the-password-0000", newPassword = NewPassword }));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, IdentityErrorCodes.PasswordIncorrect);
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{IdentityErrorCodes.PasswordIncorrect}|currentPassword" }));
        Assert.That(await RealStackSteps.StatusOfAsync(member.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));
    }
}
