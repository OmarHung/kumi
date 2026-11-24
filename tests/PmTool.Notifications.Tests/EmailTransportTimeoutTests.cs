using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PmTool.Notifications.Email;

namespace PmTool.Notifications.Tests;

/// <summary>逾時要涵蓋連線：一個收下連線卻不回應的主機，不能讓寄送一直等下去、握著鎖。</summary>
[TestFixture]
public sealed class EmailTransportTimeoutTests
{
    [Test]
    public async Task OpenAsync_ServerAcceptsButNeverGreets_FailsAsTimeoutQuickly()
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepting = listener.AcceptTcpClientAsync();
        var transport = new MailKitEmailTransport(TimeSpan.FromMilliseconds(600));
        var watch = Stopwatch.StartNew();

        // Act
        var failure = Assert.ThrowsAsync<EmailDeliveryException>(async () =>
            await transport.OpenAsync(new SmtpConnection("127.0.0.1", port, SmtpSecurity.None, null, null), CancellationToken.None));
        watch.Stop();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Kind, Is.EqualTo(EmailFailureKind.Timeout));
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        (await accepting).Dispose();
    }

    [Test]
    public async Task OpenAsync_CallerCancels_ThrowsCancellationNotATimeout()
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var transport = new MailKitEmailTransport(TimeSpan.FromSeconds(30));

        // Act & Assert
        Assert.That(
            async () => await transport.OpenAsync(new SmtpConnection("127.0.0.1", port, SmtpSecurity.None, null, null), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        await Task.CompletedTask;
    }
}
