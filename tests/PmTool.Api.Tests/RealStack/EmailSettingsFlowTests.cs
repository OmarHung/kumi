using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Tests.Support;
using PmTool.Notifications;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 寄信設定從 HTTP 一路到資料庫：真的加密、真的權限、真的人員目錄。不會真的寄信（沒有通知在等、也不按測試信）。
/// </summary>
[TestFixture]
public sealed class EmailSettingsFlowTests : RealStackTestBase
{
    private const string Route = "/v1/email-settings";

    private SignedInClient _owner = null!;

    [OneTimeSetUp]
    public async Task SignInOwnerAsync() => _owner = await SignInAsOwnerAsync();

    [OneTimeTearDown]
    public void SignOutOwner() => _owner.Dispose();

    private static object Gmail(string? password) => new
    {
        enabled = true,
        provider = "gmail",
        username = "team@example.com",
        password,
        fromName = "Example 公司",
        siteUrl = "https://pm.example.com/",
    };

    [Test]
    public async Task Owner_SavesTheSettingsAndReadsThemBackWithoutThePassword()
    {
        // Act
        using var put = await _owner.Client.PutAsync(Route, ApiRequests.Json(Gmail("abcd efgh ijkl mnop")));
        var saved = await ApiRequests.AssertSuccessEnvelopeAsync(put, HttpStatusCode.OK);
        using var get = await _owner.Client.GetAsync(Route);
        var text = await get.Content.ReadAsStringAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved?["siteUrl"]?.GetValue<string>(), Is.EqualTo("https://pm.example.com"));
            Assert.That(saved?["hasPassword"]?.GetValue<bool>(), Is.True);
            Assert.That(saved?["updatedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerName));
            Assert.That(text, Does.Not.Contain("abcdefghijklmnop").And.Not.Contain("abcd efgh"));
            Assert.That(text, Does.Contain("\"provider\":\"gmail\""));
        }
    }

    [Test]
    public async Task Owner_PasswordNullKeepsItAndEmptyClearsIt()
    {
        // Arrange
        using var first = await _owner.Client.PutAsync(Route, ApiRequests.Json(Gmail("abcdefghijklmnop")));

        // Act
        using var keep = await _owner.Client.PutAsync(Route, ApiRequests.Json(Gmail(null)));
        var kept = await ApiRequests.AssertSuccessEnvelopeAsync(keep, HttpStatusCode.OK);
        using var clear = await _owner.Client.PutAsync(Route, ApiRequests.Json(Gmail(string.Empty)));

        // Assert：清掉密碼又開著寄信，Gmail 一定要有密碼，所以被擋下來。
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            clear, HttpStatusCode.UnprocessableEntity, NotificationErrorCodes.EmailSettingsInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(kept?["hasPassword"]?.GetValue<bool>(), Is.True);
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "email.field_required|password" }));
        }
    }

    [Test]
    public async Task Member_CannotReadOrChangeTheSettings()
    {
        // Arrange
        var email = RealStackSteps.UniqueEmail("email-member");
        await RealStackSteps.CreateMemberAsync(_owner.Client, "Email Member", email);
        using var member = await SignInAsAsync(email, RealStackSteps.MemberPassword);

        // Act
        using var get = await member.Client.GetAsync(Route);
        using var put = await member.Client.PutAsync(Route, ApiRequests.Json(Gmail("abcdefghijklmnop")));
        using var test = await member.Client.PostAsync(Route + "/test", ApiRequests.Json(Gmail("abcdefghijklmnop")));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(get, HttpStatusCode.Forbidden, NotificationErrorCodes.EmailForbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(put, HttpStatusCode.Forbidden, NotificationErrorCodes.EmailForbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(test, HttpStatusCode.Forbidden, NotificationErrorCodes.EmailForbidden);
    }

    [Test]
    public async Task Member_TurnsOwnEmailNotificationsOffAndOn()
    {
        // Arrange
        var email = RealStackSteps.UniqueEmail("email-pref");
        await RealStackSteps.CreateMemberAsync(_owner.Client, "Email Pref", email);
        using var member = await SignInAsAsync(email, RealStackSteps.MemberPassword);
        const string route = "/v1/account/email-notifications";

        // Act
        var initial = await RealStackSteps.GetAsync(member.Client, route);
        using var off = await member.Client.PutAsync(route, ApiRequests.Json(new { enabled = false }));
        var afterOff = await ApiRequests.AssertSuccessEnvelopeAsync(off, HttpStatusCode.OK);
        var ownersView = await RealStackSteps.GetAsync(_owner.Client, route);
        using var on = await member.Client.PutAsync(route, ApiRequests.Json(new { enabled = true }));
        var afterOn = await ApiRequests.AssertSuccessEnvelopeAsync(on, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(initial["enabled"]?.GetValue<bool>(), Is.True);
            Assert.That(afterOff?["enabled"]?.GetValue<bool>(), Is.False);
            Assert.That(ownersView["enabled"]?.GetValue<bool>(), Is.True, "別人的開關不受影響");
            Assert.That(afterOn?["enabled"]?.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public async Task Worker_WithNothingWaiting_RunsAgainstTheRealDatabaseWithoutConnecting()
    {
        // Arrange
        using var put = await _owner.Client.PutAsync(Route, ApiRequests.Json(Gmail("abcdefghijklmnop")));

        // Act：直接叫寄送的服務，出錯會丟例外（背景工作會把例外吞掉，看不出接線有沒有錯）。
        using var scope = Factory.Services.CreateScope();
        var sent = await scope.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(CancellationToken.None);
        var workerSent = await Factory.RunEmailOnceAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.Zero);
            Assert.That(workerSent, Is.Zero);
        }
    }
}
