using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// 會改資料的請求必須帶 X-PmTool-Csrf: 1。別的網站的表單送不出自訂標頭，跨來源的 fetch 要帶它又得先過預檢，
/// 所以它是 SameSite Cookie 之外的第二道防線。
/// </summary>
[TestFixture]
public sealed class CsrfTests
{
    [Test]
    public async Task SignIn_WithoutCsrfHeader_Returns403WithoutCallingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(
            Sessions.Route, Sessions.Credentials(TestUsers.Owner.Email, TestUsers.Password));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
        Assert.That(factory.Modules.Authentication.SignInCalls, Is.Empty);
    }

    [Test]
    public async Task ProtectedWrite_NotSignedInAndWithoutCsrfHeader_ReturnsCsrfRequiredRatherThanAuthRequired()
    {
        // Arrange：防偽檢查只看請求本身，排在確認登入之前，所以兩個都不對時回的是它。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(
            "/v1/projects", ApiRequests.Json(new { key = "WEB", name = "Example", template = "simple_tasks" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
    }

    [TestCase("DELETE")]
    [TestCase("PUT")]
    [TestCase("PATCH")]
    [TestCase("POST")]
    public async Task UnsafeMethodUnderV1_SignedInWithoutCsrfHeader_Returns403(string method)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var cookie = await SignInWithHeaderAsync(client);
        using var request = new HttpRequestMessage(new HttpMethod(method), Sessions.Route);
        request.Headers.Add("Cookie", cookie);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
    }

    [TestCase("DELETE", "/v1/items/WEB-1")]
    [TestCase("POST", "/v1/items/WEB-1/type")]
    [TestCase("DELETE", "/v1/projects/WEB/members/0190a000-0000-7000-8000-0000000000e5")]
    public async Task AdminWrite_SignedInWithoutCsrfHeader_Returns403WithoutCallingTheModule(string method, string route)
    {
        // Arrange：刪除項目、改類型、移除成員和其他寫入一樣要帶防偽標頭。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var cookie = await SignInWithHeaderAsync(client);
        using var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = ApiRequests.Json(new { type = "task" }) };
        request.Headers.Add("Cookie", cookie);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.Items.DeleteCalls, Is.Empty);
            Assert.That(factory.Modules.Items.ChangeTypeCalls, Is.Empty);
            Assert.That(factory.Modules.Projects.RemoveMemberCalls, Is.Empty);
        }
    }

    [TestCase("POST", "/v1/people/0190a000-0000-7000-8000-0000000000b2/disable")]
    [TestCase("POST", "/v1/people/0190a000-0000-7000-8000-0000000000b2/enable")]
    [TestCase("PUT", "/v1/people/0190a000-0000-7000-8000-0000000000b2/password")]
    [TestCase("PUT", "/v1/people/0190a000-0000-7000-8000-0000000000b2/role")]
    [TestCase("PUT", "/v1/account/password")]
    [TestCase("DELETE", "/v1/account/other-sessions")]
    [TestCase("POST", "/v1/client-errors")]
    public async Task AccountWrite_SignedInWithoutCsrfHeader_Returns403WithoutCallingTheModule(string method, string route)
    {
        // Arrange：停用、重設密碼、改自己的密碼、登出其他裝置、回報錯誤都會改資料或寫記錄，要帶防偽標頭。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var cookie = await SignInWithHeaderAsync(client);
        using var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = ApiRequests.Json(new { }) };
        request.Headers.Add("Cookie", cookie);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.People.SetDisabledCalls, Is.Empty);
            Assert.That(factory.Modules.People.ResetPasswordCalls, Is.Empty);
            Assert.That(factory.Modules.People.SetRoleCalls, Is.Empty);
            Assert.That(factory.Modules.Accounts.Calls, Is.Empty);
            Assert.That(factory.Modules.Sessions.EndOthersCalls, Is.Empty);
        }
    }

    [TestCase("0")]
    [TestCase("true")]
    [TestCase("")]
    public async Task SignIn_CsrfHeaderWithAnotherValue_Returns403(string value)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Sessions.Route)
        {
            Content = Sessions.Credentials(TestUsers.Owner.Email, TestUsers.Password),
        };
        request.Headers.TryAddWithoutValidation(ApiRequests.CsrfHeader, value);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
    }

    [TestCase(ApiRequests.BlueprintRoute)]
    [TestCase(ApiRequests.ChangeSetRoute)]
    public async Task ValidationEndpoints_WithoutCsrfHeader_StillWork(string route)
    {
        // Arrange：兩個驗證端點是匿名的純函式，不讀寫資料，沒有可以被冒用的東西。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(route, ApiRequests.Document("{}"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SafeMethod_WithoutCsrfHeader_IsNotChecked()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var cookie = await SignInWithHeaderAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Get, Sessions.Route);
        request.Headers.Add("Cookie", cookie);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task UnsafeMethodOutsideV1_WithoutCsrfHeader_IsNotChecked()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync("/no-such-route", ApiRequests.Json(new { }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
    }

    private static async Task<string> SignInWithHeaderAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Sessions.Route)
        {
            Content = Sessions.Credentials(TestUsers.Member.Email, TestUsers.Password),
        };
        request.Headers.Add(ApiRequests.CsrfHeader, "1");
        using var response = await client.SendAsync(request);
        return Sessions.SessionCookieOf(response) ?? throw new AssertionException("登入沒有發 Cookie");
    }
}
