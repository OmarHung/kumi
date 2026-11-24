using PmTool.Identity.OAuth;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>導回網址的規定、比對與組出導回網址。</summary>
[TestFixture]
public sealed class OAuthRedirectsTests
{
    [TestCase("https://app.example.com/cb", true)]
    [TestCase("https://app.example.com:8443/cb?x=1", true)]
    [TestCase("http://127.0.0.1:5000/cb", true)]
    [TestCase("http://[::1]:5000/cb", true)]
    [TestCase("http://localhost/cb", true)]
    [TestCase("http://LOCALHOST:1/cb", true)]
    [TestCase("http://app.example.com/cb", false)]
    [TestCase("http://127.0.0.2/cb", false)]
    [TestCase("myapp://cb", false)]
    [TestCase("javascript:alert(1)", false)]
    [TestCase("https://app.example.com/cb#x", false)]
    [TestCase("https://u@app.example.com/cb", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void TryParse_AcceptsOnlyHttpsOrLoopbackHttp(string? value, bool expected)
    {
        Assert.That(OAuthRedirects.TryParse(value, out _), Is.EqualTo(expected));
    }

    [Test]
    public void Matches_HttpsNeedsTheExactString()
    {
        // Arrange
        string[] registered = ["https://app.example.com/cb"];

        // Act & Assert
        Assert.That(Matches(registered, "https://app.example.com/cb"), Is.True);
        Assert.That(Matches(registered, "https://app.example.com/cb/"), Is.False);
        Assert.That(Matches(registered, "https://APP.example.com/cb"), Is.False, "不做任何正規化");
        Assert.That(Matches(registered, "https://app.example.com:443/cb"), Is.False);
        Assert.That(Matches(registered, "https://app.example.com/cb?x=1"), Is.False);
    }

    [Test]
    public void Matches_LoopbackIgnoresThePortButNotTheSchemeHostOrPath()
    {
        // Arrange
        string[] registered = ["http://127.0.0.1/cb?mode=a"];

        // Act & Assert
        Assert.That(Matches(registered, "http://127.0.0.1:61234/cb?mode=a"), Is.True);
        Assert.That(Matches(registered, "http://127.0.0.1:61234/cb?mode=b"), Is.False);
        Assert.That(Matches(registered, "http://127.0.0.1:61234/other?mode=a"), Is.False);
        Assert.That(Matches(registered, "http://localhost:61234/cb?mode=a"), Is.False);
        Assert.That(Matches(registered, "https://127.0.0.1:61234/cb?mode=a"), Is.False);
    }

    [Test]
    public void Matches_ARegisteredLoopbackNeverMatchesARemoteHost()
    {
        Assert.That(Matches(["http://127.0.0.1/cb"], "https://evil.example.org/cb"), Is.False);
    }

    [TestCase("https://app.example.com/cb", "app.example.com")]
    [TestCase("https://app.example.com:8443/cb", "app.example.com:8443")]
    [TestCase("http://127.0.0.1:5000/cb", "127.0.0.1:5000")]
    [TestCase("http://[::1]:5000/cb", "[::1]:5000")]
    public void HostOf_ShowsTheHostAndANonDefaultPort(string redirect, string expected)
    {
        Assert.That(OAuthRedirects.TryParse(redirect, out var uri), Is.True);
        Assert.That(OAuthRedirects.HostOf(uri!), Is.EqualTo(expected));
    }

    [Test]
    public void HostOf_LookalikeUnicodeHost_IsShownAsPunycode()
    {
        // Arrange：第一個字母是西里爾字母的 а（U+0430），不是拉丁字母的 a。
        const string lookalike = "https://\u0430pp.example.com:8443/cb";
        Assert.That(OAuthRedirects.TryParse(lookalike, out var uri), Is.True);

        // Act
        var host = OAuthRedirects.HostOf(uri!);

        // Assert
        Assert.That(host, Is.EqualTo("xn--pp-6kc.example.com:8443"));
        Assert.That(host.All(char.IsAscii), Is.True);
    }

    [Test]
    public void Append_AddsEscapedParametersAndSkipsNulls()
    {
        // Act
        var plain = OAuthRedirects.Append("http://127.0.0.1:1/cb", [("code", "a b&c"), ("state", null), ("iss", "https://x")]);
        var withQuery = OAuthRedirects.Append("https://a.example.com/cb?t=1", [("code", "c")]);

        // Assert
        Assert.That(plain, Is.EqualTo("http://127.0.0.1:1/cb?code=a%20b%26c&iss=https%3A%2F%2Fx"));
        Assert.That(withQuery, Is.EqualTo("https://a.example.com/cb?t=1&code=c"));
    }

    private static bool Matches(string[] registered, string requested)
    {
        Assert.That(OAuthRedirects.TryParse(requested, out var uri), Is.True);
        return OAuthRedirects.Matches(registered, uri!);
    }
}
