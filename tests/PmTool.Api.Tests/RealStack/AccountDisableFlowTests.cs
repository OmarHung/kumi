using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 擁有者停用、重新啟用、重設別人的密碼，從 HTTP 一路到資料庫。擁有者在開始時登入一次，大家共用：
/// TestServer 沒有來源 IP，所有登入共用每分鐘 <see cref="ApiLimits.SignInPermitsPerWindow"/> 次的額度。
/// </summary>
[TestFixture]
public sealed class AccountDisableFlowTests : RealStackTestBase
{
    private const string ResetTo = "real-stack-reset-0000";

    private SignedInClient _owner = null!;

    [OneTimeSetUp]
    public async Task SignInOwnerAsync() => _owner = await SignInAsOwnerAsync();

    [OneTimeTearDown]
    public void SignOutOwner() => _owner.Dispose();

    [Test]
    public async Task Disable_TheirCookieIsRejectedAtOnceAndTheyCannotSignIn()
    {
        // Arrange
        var (personId, email) = await CreateMemberAsync("disable");
        using var member = await SignInAsAsync(email, RealStackSteps.MemberPassword);
        Assert.That(await RealStackSteps.StatusOfAsync(member.Client, "/v1/projects"), Is.EqualTo(HttpStatusCode.OK));

        // Act
        using var response = await _owner.Client.PostAsync($"/v1/people/{personId}/disable", null);
        var person = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert：還在人員清單裡，標著停用。
        using var anonymous = Factory.CreateApiClient();
        var people = await RealStackSteps.GetAsync(_owner.Client, "/v1/people");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(person?["disabled"]?.GetValue<bool>(), Is.True);
            Assert.That(await RealStackSteps.StatusOfAsync(member.Client, "/v1/projects"), Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(await RealStackSteps.TrySignInAsync(anonymous, email, RealStackSteps.MemberPassword), Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(
                people.AsArray().Single(p => p?["personId"]?.GetValue<Guid>() == personId)?["disabled"]?.GetValue<bool>(),
                Is.True);
        }
    }

    [Test]
    public async Task Enable_AfterDisable_TheyCanSignInAgain()
    {
        // Arrange
        var (personId, email) = await CreateMemberAsync("enable");
        using var disabled = await _owner.Client.PostAsync($"/v1/people/{personId}/disable", null);
        Assert.That(disabled.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act
        using var response = await _owner.Client.PostAsync($"/v1/people/{personId}/enable", null);
        var person = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(person?["disabled"]?.GetValue<bool>(), Is.False);
        using var member = await SignInAsAsync(email, RealStackSteps.MemberPassword);
        Assert.That(await RealStackSteps.StatusOfAsync(member.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ResetPassword_TheirSessionsEndAndOnlyTheNewPasswordWorks()
    {
        // Arrange
        var (personId, email) = await CreateMemberAsync("reset");
        using var member = await SignInAsAsync(email, RealStackSteps.MemberPassword);
        // 先查一次，結果進了快取：作廢要連快取一起清，下一個請求才會失效。
        Assert.That(await RealStackSteps.StatusOfAsync(member.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));

        // Act
        using var response = await _owner.Client.PutAsync($"/v1/people/{personId}/password", ApiRequests.Json(new { newPassword = ResetTo }));
        var person = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert：擁有者自己的工作階段不受影響。
        using var anonymous = Factory.CreateApiClient();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(person?["personId"]?.GetValue<Guid>(), Is.EqualTo(personId));
            Assert.That(await RealStackSteps.StatusOfAsync(member.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(await RealStackSteps.StatusOfAsync(_owner.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await RealStackSteps.TrySignInAsync(anonymous, email, ResetTo), Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task Disable_TheirOwnAccount_Returns409AndTheyStaySignedIn()
    {
        // Act：永遠至少留著一位能登入的擁有者。
        using var response = await _owner.Client.PostAsync($"/v1/people/{_owner.PersonId}/disable", null);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, IdentityErrorCodes.IsSelf);
        Assert.That(await RealStackSteps.StatusOfAsync(_owner.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Disable_ByAMember_Returns403()
    {
        // Arrange：網頁不給一般成員這顆按鈕，但伺服器的檢查才算數。
        var (_, email) = await CreateMemberAsync("member");
        var (targetId, _) = await CreateMemberAsync("target");
        using var member = await SignInAsAsync(email, RealStackSteps.MemberPassword);

        // Act
        using var response = await member.Client.PostAsync($"/v1/people/{targetId}/disable", null);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, IdentityErrorCodes.Forbidden);
    }

    private async Task<(Guid PersonId, string Email)> CreateMemberAsync(string prefix)
    {
        var email = RealStackSteps.UniqueEmail(prefix);
        var person = await RealStackSteps.CreateMemberAsync(_owner.Client, $"{prefix} Example", email);
        return (person["personId"]!.GetValue<Guid>(), email);
    }
}
