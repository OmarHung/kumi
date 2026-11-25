using System.Net;
using PmTool.Api.Endpoints;

namespace PmTool.Api.Tests;

/// <summary>瀏覽器登入的來源鍵：同一台電腦的兩種 loopback 寫法算同一個來源，不同網路位址算不同來源。</summary>
[TestFixture]
public sealed class CliLoginOriginTests
{
    [Test]
    public void KeyOf_Ipv4AndIpv6Loopback_AreTheSameKey()
    {
        // Act
        var keys = new[]
        {
            CliLoginOrigin.KeyOf(IPAddress.Loopback),
            CliLoginOrigin.KeyOf(IPAddress.IPv6Loopback),
            CliLoginOrigin.KeyOf(IPAddress.Loopback.MapToIPv6()),
            CliLoginOrigin.KeyOf(IPAddress.Parse("127.0.0.2")),
        };

        // Assert
        Assert.That(keys, Has.All.EqualTo(keys[0]));
    }

    [Test]
    public void KeyOf_TwoDifferentIpv4Addresses_AreDifferentKeys()
    {
        // Act
        var first = CliLoginOrigin.KeyOf(IPAddress.Parse("203.0.113.7"));
        var second = CliLoginOrigin.KeyOf(IPAddress.Parse("203.0.113.8"));

        // Assert
        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void KeyOf_TwoIpv6AddressesInTheSame64_AreTheSameKey()
    {
        // Act
        var first = CliLoginOrigin.KeyOf(IPAddress.Parse("2001:db8:1:2::1"));
        var second = CliLoginOrigin.KeyOf(IPAddress.Parse("2001:db8:1:2:aaaa::2"));
        var other = CliLoginOrigin.KeyOf(IPAddress.Parse("2001:db8:1:3::1"));

        // Assert
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Is.Not.EqualTo(other));
    }

    [Test]
    public void KeyOf_WithoutARemoteAddress_IsEmptySoNoOriginMatches()
    {
        // Act
        var key = CliLoginOrigin.KeyOf(null);

        // Assert
        Assert.That(key, Is.Empty);
    }

    [Test]
    public void KeyOf_WithAnAddress_IsNeverBlankAndFitsTheColumn()
    {
        // Act
        var keys = new[] { CliLoginOrigin.KeyOf(IPAddress.Parse("2001:db8:1:2::1")), CliLoginOrigin.KeyOf(IPAddress.Parse("203.0.113.7")), CliLoginOrigin.KeyOf(IPAddress.Loopback) };

        // Assert
        Assert.That(keys, Has.All.Not.Empty.And.Property("Length").LessThanOrEqualTo(64));
    }
}
