using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Api.RateLimiting;
using PmTool.Api.Security;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>請求的來源：身分模組的稽核與通知記「從哪裡」時用。</summary>
[TestFixture]
public sealed class HttpRequestSourceTests
{
    [Test]
    public void Current_NoRequest_IsNull()
    {
        // Arrange
        var source = new HttpRequestSource(new HttpContextAccessor());

        // Act & Assert
        Assert.That(source.Current, Is.Null);
    }

    [TestCase("203.0.113.5", "203.0.113.5", TestName = "Current_IPv4Address_IsKept")]
    [TestCase("::ffff:203.0.113.5", "203.0.113.5", TestName = "Current_IPv4MappedAddress_IsConvertedBackToIPv4")]
    [TestCase("2001:db8::1", "2001:db8::1", TestName = "Current_IPv6Address_IsKept")]
    public void Current_RemoteAddress_IsWrittenTheSameWayForBothFamilies(string remote, string expected)
    {
        // Arrange
        var source = SourceFor(context => context.Connection.RemoteIpAddress = IPAddress.Parse(remote));

        // Act
        var current = source.Current;

        // Assert
        Assert.That(current?.Address, Is.EqualTo(expected));
    }

    [Test]
    public void Current_NoRemoteAddress_HasNoAddressButStillTheRequest()
    {
        // Arrange
        var source = SourceFor(context => context.Request.Headers.UserAgent = "example-agent/1.0");

        // Act
        var current = source.Current;

        // Assert
        Assert.That((current?.Address, current?.UserAgent), Is.EqualTo(((string?)null, "example-agent/1.0")));
    }

    [Test]
    public void Current_LongUserAgent_IsCutAtTheLimit()
    {
        // Arrange
        var source = SourceFor(context => context.Request.Headers.UserAgent = new string('a', 1000));

        // Act
        var userAgent = source.Current?.UserAgent;

        // Assert
        Assert.That(userAgent, Has.Length.EqualTo(RequestSourceText.MaxUserAgentLength));
    }

    [Test]
    public void Current_CuttingInTheMiddleOfASurrogatePair_DropsTheHalf()
    {
        // Arrange：第 256 個位置剛好是一對代理字元的前半
        var source = SourceFor(context => context.Request.Headers.UserAgent = new string('a', RequestSourceText.MaxUserAgentLength - 1) + "😀😀");

        // Act
        var userAgent = source.Current?.UserAgent;

        // Assert
        Assert.That(userAgent, Is.EqualTo(new string('a', RequestSourceText.MaxUserAgentLength - 1)));
    }

    [Test]
    public void Current_ControlAndFormatCharacters_BecomeSpaces()
    {
        // Arrange：NUL 進不了 PostgreSQL；零寬與雙向控制字元會讓紀錄看起來和實際不同
        var source = SourceFor(context => context.Request.Headers.UserAgent = "agent\0one\ttwo​three‮four");

        // Act
        var userAgent = source.Current?.UserAgent;

        // Assert
        Assert.That(userAgent, Is.EqualTo("agent one two three four"));
    }

    [Test]
    public void Current_LoneSurrogate_IsReplaced()
    {
        // Arrange：落單的代理字元轉成 UTF-8 會失敗，整筆紀錄就寫不進去
        var source = SourceFor(context => context.Request.Headers.UserAgent = "bad\uD800worse");

        // Act
        var userAgent = source.Current?.UserAgent;

        // Assert
        Assert.That(userAgent, Is.EqualTo("bad worse"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\0​")]
    public void Current_EmptyOrOnlyInvisibleUserAgent_IsNull(string? userAgent)
    {
        // Arrange
        var source = SourceFor(context =>
        {
            if (userAgent is not null)
            {
                context.Request.Headers.UserAgent = userAgent;
            }
        });

        // Act & Assert
        Assert.That(source.Current?.UserAgent, Is.Null);
    }

    [Test]
    public void Application_ResolvesTheRequestSourceFromTheHttpContext()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act
        var source = factory.Services.GetRequiredService<IRequestSource>();

        // Assert
        Assert.That(source, Is.InstanceOf<HttpRequestSource>());
    }

    [Test]
    public void Application_RegistersTheSignInThrottleContractsAndTestsDisableIt()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act
        var limits = factory.Services.GetRequiredService<SignInThrottleLimits>();
        var delay = factory.Services.GetRequiredService<ISignInDelay>();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(limits.IsEnabled, Is.False);
            Assert.That(delay, Is.InstanceOf<TimeProviderSignInDelay>());
        }
    }

    private static HttpRequestSource SourceFor(Action<HttpContext> configure)
    {
        var context = new DefaultHttpContext();
        configure(context);
        return new HttpRequestSource(new HttpContextAccessor { HttpContext = context });
    }
}
