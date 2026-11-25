using System.Security.Cryptography;
using System.Text;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests.RealStack;

/// <summary>瀏覽器登入的測試共用的請求：帶指定的遠端位址（沒給就維持沒有位址）、一組 PKCE。</summary>
internal static class CliLoginRequests
{
    public const string Approve = "/v1/cli-login/approve";
    public const string Token = "/v1/cli-login/token";

    public static HttpRequestMessage From(string route, string? address, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = ApiRequests.Json(body) };
        if (address is not null)
        {
            request.Headers.Add(ApiFactory.RemoteIpHeader, address);
        }

        return request;
    }

    public static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() + "abc";
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, challenge);
    }

    public static object ApproveBody(string challenge) => new
    {
        port = 53682, state = "abcdefghijklmnopqrstuv", challenge, machine = "MacBook-Pro", tokens = new[] { "cli" },
        scopes = new[] { "items:read" }, grantedScopes = new[] { "items:read" }, expiresInDays = 30,
        confirmationCode = CliLoginRules.ConfirmationCodeOf(challenge),
    };
}
