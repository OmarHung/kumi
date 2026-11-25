using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 瀏覽器登入的來源比對（真的 HTTP 與身分模組）：碼只換給和核准時同一個來源鍵的請求。
/// 假的身分模組只會照設定回，來源有沒有真的比對只有這裡看得到。
/// </summary>
[TestFixture]
public sealed class CliLoginOriginFlowTests : RealStackTestBase
{
    [Test]
    public async Task Redeem_FromAnotherAddress_IsRejectedAndTheCodeIsBurned()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var (verifier, code) = await ApproveFromAsync(owner, "203.0.113.7");

        // Act
        using var wrong = await RedeemFromAsync(code, verifier, "198.51.100.9");
        using var again = await RedeemFromAsync(code, verifier, "203.0.113.7");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertFailureEnvelopeAsync(wrong, (HttpStatusCode)422, IdentityErrorCodes.CliLoginCodeInvalid);
            await ApiRequests.AssertFailureEnvelopeAsync(again, (HttpStatusCode)422, IdentityErrorCodes.CliLoginCodeInvalid);
        }
    }

    [Test]
    public async Task Redeem_Ipv6LoopbackApprovedAndIpv4LoopbackRedeemed_Succeeds()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var (verifier, code) = await ApproveFromAsync(owner, "::1");

        // Act
        using var redeemed = await RedeemFromAsync(code, verifier, "127.0.0.1");

        // Assert
        Assert.That(redeemed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Redeem_FromTheSameIpv6Prefix_Succeeds()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var (verifier, code) = await ApproveFromAsync(owner, "2001:db8:1:2::1");

        // Act
        using var redeemed = await RedeemFromAsync(code, verifier, "2001:db8:1:2:aaaa::2");

        // Assert
        Assert.That(redeemed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Redeem_FromAnotherIpv6Prefix_IsRejected()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var (verifier, code) = await ApproveFromAsync(owner, "2001:db8:1:2::1");

        // Act
        using var redeemed = await RedeemFromAsync(code, verifier, "2001:db8:1:3::1");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(redeemed, (HttpStatusCode)422, IdentityErrorCodes.CliLoginCodeInvalid);
    }

    [Test]
    public async Task Approve_WithoutARemoteAddress_IsRejected()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var (_, challenge) = CliLoginRequests.NewPkce();

        // Act
        using var response = await owner.Client.SendAsync(
            CliLoginRequests.From(CliLoginRequests.Approve, address: null, CliLoginRequests.ApproveBody(challenge)));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, (HttpStatusCode)422, IdentityErrorCodes.CliLoginRequestInvalid);
        Assert.That(details.ToJsonString(), Does.Contain("origin"));
    }

    private static async Task<(string Verifier, string Code)> ApproveFromAsync(SignedInClient owner, string address)
    {
        var (verifier, challenge) = CliLoginRequests.NewPkce();
        using var approve = await owner.Client.SendAsync(
            CliLoginRequests.From(CliLoginRequests.Approve, address, CliLoginRequests.ApproveBody(challenge)));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(approve, HttpStatusCode.OK);
        var code = System.Web.HttpUtility.ParseQueryString(new Uri(data!["redirectTo"]!.GetValue<string>()).Query)["code"]!;
        return (verifier, code);
    }

    private Task<HttpResponseMessage> RedeemFromAsync(string code, string verifier, string address)
    {
        var client = Factory.CreateClient();
        return client.SendAsync(CliLoginRequests.From(CliLoginRequests.Token, address, new { code, codeVerifier = verifier }));
    }
}
