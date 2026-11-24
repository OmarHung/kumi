using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>登出與登出其他裝置在伺服器端作廢：事先複製走的 Cookie 也跟著失效。</summary>
[TestFixture]
public sealed class SignOutFlowTests : RealStackTestBase
{
    [Test]
    public async Task SignOut_TheSameCookieAfterwards_Returns401()
    {
        // Arrange：用不記 Cookie 的用戶端，登出之後還能把同一份 Cookie 再送一次，模擬被複製走的 Cookie。
        using var client = Sessions.CreateReplayClient(Factory);
        var cookie = await RealStackSteps.SignInForCookieAsync(client, RealStackFactory.OwnerEmail, RealStackFactory.OwnerPassword);
        var before = await Sessions.SendWithCookieAsync(client, Sessions.Route, cookie);

        // Act
        using var signOut = Sessions.WithCookie(HttpMethod.Delete, Sessions.Route, cookie);
        using var signOutResponse = await client.SendAsync(signOut);
        var after = await Sessions.SendWithCookieAsync(client, Sessions.Route, cookie);

        // Assert
        Assert.That(before, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await ApiRequests.AssertSuccessEnvelopeAsync(signOutResponse, HttpStatusCode.OK), Is.Null);
        Assert.That(after, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task EndOtherSessions_SignsOutTheOtherBrowsersRightAwayAndKeepsThisOne()
    {
        // Arrange
        using var current = await SignInAsOwnerAsync();
        using var second = await SignInAsOwnerAsync();
        using var third = await SignInAsOwnerAsync();
        // 先查一次，結果進了快取：作廢要連快取一起清，下一個請求才會失效。
        Assert.That(await RealStackSteps.StatusOfAsync(second.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await RealStackSteps.StatusOfAsync(third.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));

        // Act
        using var response = await current.Client.DeleteAsync("/v1/account/other-sessions");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        using var again = await current.Client.DeleteAsync("/v1/account/other-sessions");
        var none = await ApiRequests.AssertSuccessEnvelopeAsync(again, HttpStatusCode.OK);

        // Assert：別的測試登出過的工作階段已經作廢，不算在裡面；第二次就沒有別的可以登出了。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["signedOutSessions"]?.GetValue<int>(), Is.EqualTo(2));
            Assert.That(none?["signedOutSessions"]?.GetValue<int>(), Is.Zero);
            Assert.That(await RealStackSteps.StatusOfAsync(current.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await RealStackSteps.StatusOfAsync(second.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(await RealStackSteps.StatusOfAsync(third.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.Unauthorized));
        }
    }
}
