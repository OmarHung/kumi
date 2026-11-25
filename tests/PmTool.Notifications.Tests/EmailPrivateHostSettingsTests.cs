using PmTool.Abstractions;
using PmTool.Notifications.Email;

namespace PmTool.Notifications.Tests;

/// <summary>不允許連內網時，存設定與寄測試信都在第一時間擋下內網與本機的位址，而且訊息不帶對方主機回的原文。</summary>
[TestFixture]
public sealed class EmailPrivateHostSettingsTests : EmailTestBase
{
    private static EmailSettingsInput SmtpAt(string host) => SmtpInput() with { Host = host };

    [TestCase("127.0.0.1")]
    [TestCase("10.0.0.5")]
    [TestCase("172.17.0.2")]
    [TestCase("192.168.1.10")]
    [TestCase("169.254.169.254")]
    [TestCase("0.0.0.0")]
    public async Task Save_PrivateIpv4Literal_IsRejectedOnTheHostField(string host)
    {
        // Act
        var result = await Host.EmailSettings.SaveAsync(Admin, SmtpAt(host), CancellationToken.None);

        // Assert
        var detail = result.Failure!.Details!.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure.Code, Is.EqualTo(NotificationErrorCodes.EmailSettingsInvalid));
            Assert.That(detail.Code, Is.EqualTo(NotificationErrorCodes.EmailFieldInvalid));
            Assert.That(detail.Path, Is.EqualTo("host"));
            Assert.That(detail.Message, Is.EqualTo("不能是內網或本機的位址。"));
        }
    }

    [Test]
    public async Task Save_PublicIpv4LiteralOrHostName_IsAccepted()
    {
        // Act
        var literal = await Host.EmailSettings.SaveAsync(Admin, SmtpAt("8.8.8.8"), CancellationToken.None);
        var name = await Host.EmailSettings.SaveAsync(Admin, SmtpAt("smtp.example.com"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(literal.Succeeded, Is.True, literal.Failure?.Message);
            Assert.That(name.Succeeded, Is.True, name.Failure?.Message);
        }
    }

    [Test]
    public async Task SendTest_PrivateIpv4Literal_IsRejectedBeforeAnyConnection()
    {
        // Act
        var result = await Host.EmailSettings.SendTestAsync(Admin, SmtpAt("10.0.0.5"), "me@example.com", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure!.Details!.Single().Path, Is.EqualTo("host"));
            Assert.That(Transport.Connections, Is.Empty);
        }
    }

    [Test]
    public async Task SendTest_ServerResponseWithInternalDetails_IsNotShownToTheAdmin()
    {
        // Arrange
        Transport.OnOpen = _ => new EmailDeliveryException(
            EmailFailureKind.Other, "對方主機拒絕了連線", "554 5.7.1 relay.corp.internal refused 10.1.2.3", statusCode: 554);

        // Act
        var result = await Host.EmailSettings.SendTestAsync(Admin, SmtpInput(), "me@example.com", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Is.EqualTo("對方主機拒絕了連線（554）"));
            Assert.That(result.Failure.Message, Does.Not.Contain("corp.internal").And.Not.Contain("10.1.2.3"));
        }
    }
}

/// <summary>部署明確允許內網的轉信主機時，內網位址照樣能存。</summary>
[TestFixture]
public sealed class EmailPrivateHostAllowedSettingsTests : EmailTestBase
{
    protected override bool AllowPrivateHosts => true;

    [Test]
    public async Task Save_PrivateIpv4Literal_IsAcceptedWhenTheDeploymentAllowsIt()
    {
        // Act
        var result = await Host.EmailSettings.SaveAsync(Admin, SmtpInput() with { Host = "10.0.0.5" }, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }
}
