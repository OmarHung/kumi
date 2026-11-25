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
        var transport = new MailKitEmailTransport(TimeSpan.FromMilliseconds(600), allowPrivateHosts: true);
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
        var transport = new MailKitEmailTransport(TimeSpan.FromSeconds(30), allowPrivateHosts: true);

        // Act & Assert
        Assert.That(
            async () => await transport.OpenAsync(new SmtpConnection("127.0.0.1", port, SmtpSecurity.None, null, null), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        await Task.CompletedTask;
    }

    [Test]
    public async Task OpenAsync_CallerCancelsDuringLogin_ReleasesTheConnection()
    {
        // Arrange：假的 SMTP 主機打完招呼、說支援 AUTH，登入時就不回應；呼叫端在這時取消。
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clientClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var authReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync();
            using var stream = accepted.GetStream();
            using var reader = new StreamReader(stream);
            await using var writer = new StreamWriter(stream) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 example.test ready");
            try
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250-example.test");
                        await writer.WriteLineAsync("250 AUTH PLAIN");
                    }
                    else if (line.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase))
                    {
                        authReceived.TrySetResult();
                    }
                }

                // 讀到結尾：用戶端把連線關掉了。
                clientClosed.TrySetResult(true);
            }
            catch (IOException)
            {
                clientClosed.TrySetResult(true);
            }
        });
        using var cancellation = new CancellationTokenSource();
        var transport = new MailKitEmailTransport(TimeSpan.FromSeconds(30), allowPrivateHosts: true);

        // Act
        var opening = transport.OpenAsync(new SmtpConnection("127.0.0.1", port, SmtpSecurity.None, "user", "secret"), cancellation.Token);
        await authReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        // Assert
        Assert.That(async () => await opening, Throws.InstanceOf<OperationCanceledException>());
        var closed = await Task.WhenAny(clientClosed.Task, Task.Delay(TimeSpan.FromSeconds(10))) == clientClosed.Task;
        Assert.That(closed, Is.True, "取消之後連線要被釋放，不能留在背景");
    }
}
