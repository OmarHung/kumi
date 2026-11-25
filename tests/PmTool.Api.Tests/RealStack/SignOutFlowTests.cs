using System.Net;
using Npgsql;
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
        using var response = await current.Client.PostAsync("/v1/account/other-sessions/sign-out", ApiRequests.Json(new { currentPassword = RealStackFactory.OwnerPassword }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        using var again = await current.Client.PostAsync("/v1/account/other-sessions/sign-out", ApiRequests.Json(new { currentPassword = RealStackFactory.OwnerPassword }));
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

    [Test]
    public async Task EndOtherSessions_WrongPassword_Returns422AndSignsNobodyOut()
    {
        // Arrange
        using var current = await SignInAsOwnerAsync();
        using var second = await SignInAsOwnerAsync();

        // Act
        using var response = await current.Client.PostAsync("/v1/account/other-sessions/sign-out", ApiRequests.Json(new { currentPassword = "wrong-password" }));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, "account.password_incorrect");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "account.password_incorrect|currentPassword" }));
            Assert.That(await RealStackSteps.StatusOfAsync(second.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task StaleCookieAfterEndOtherSessions_Returns401WithoutDeletingTheNewCookie()
    {
        // Arrange：用不自動記 Cookie 的用戶端，換發之後還能再送一次舊的
        using var client = Sessions.CreateReplayClient(Factory);
        var oldCookie = await RealStackSteps.SignInForCookieAsync(client, RealStackFactory.OwnerEmail, RealStackFactory.OwnerPassword);
        using var rotate = Sessions.WithCookie(HttpMethod.Post, "/v1/account/other-sessions/sign-out", oldCookie);
        rotate.Content = ApiRequests.Json(new { currentPassword = RealStackFactory.OwnerPassword });
        using var rotated = await client.SendAsync(rotate);
        var newCookie = Sessions.SessionCookieOf(rotated)!;

        // Act
        using var stale = Sessions.WithCookie(HttpMethod.Get, Sessions.Route, oldCookie);
        using var staleResponse = await client.SendAsync(stale);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(staleResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(Sessions.SetCookieLineOf(staleResponse), Is.Null);
            Assert.That(await Sessions.SendWithCookieAsync(client, Sessions.Route, newCookie), Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task CreateAccessToken_SessionEndedBehindTheCache_Returns401AndNothingIsCreated()
    {
        // Arrange：別的程序作廢了這個工作階段，這個程序的快取還認它（最多 30 秒）
        using var stolen = await SignInAsOwnerAsync();
        Assert.That(await RealStackSteps.StatusOfAsync(stolen.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));
        await EndSessionsBehindTheCacheAsync(DateTimeOffset.UtcNow.AddMinutes(-1));

        // Act
        using var response = await stolen.Client.PostAsync(
            "/v1/account/tokens", ApiRequests.Json(new { name = "web", scopes = new[] { "items:read" }, expiresInDays = 30 }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task ApproveCliLogin_SessionEndedBehindTheCache_Returns401()
    {
        // Arrange
        using var stolen = await SignInAsOwnerAsync();
        Assert.That(await RealStackSteps.StatusOfAsync(stolen.Client, Sessions.Route), Is.EqualTo(HttpStatusCode.OK));
        await EndSessionsBehindTheCacheAsync(DateTimeOffset.UtcNow.AddMinutes(-1));
        var (_, challenge) = CliLoginRequests.NewPkce();

        // Act
        using var response = await stolen.Client.SendAsync(CliLoginRequests.From(CliLoginRequests.Approve, "203.0.113.5", CliLoginRequests.ApproveBody(challenge)));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    /// <summary>直接寫資料庫作廢最近開的工作階段，不經過模組：這個程序的快取不知道。</summary>
    private async Task EndSessionsBehindTheCacheAsync(DateTimeOffset since)
    {
        await using var connection = new NpgsqlConnection(Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE identity.sessions SET revoked_at = now(), revoked_reason = 'test' WHERE revoked_at IS NULL AND created_at >= @since", connection);
        command.Parameters.AddWithValue("since", since);
        await command.ExecuteNonQueryAsync();
    }
}
