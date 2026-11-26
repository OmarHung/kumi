using System.Net.Sockets;
using System.Text;
using PmTool.Cli.Login;

namespace PmTool.Cli.Tests.Security;

[TestFixture]
public sealed class LoopbackReceiverHardeningTests
{
    private const string State = "state-value-from-the-test";
    private static readonly TimeSpan ShortGrace = TimeSpan.FromMilliseconds(200);

    private static async Task<string> GetAsync(int port, string target)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    [TestCase("a b")]
    [TestCase("%3Cscript%3E")]
    [TestCase("a%00b")]
    public async Task Receiver_ACodeWithTheRightStateButAnImpossibleFormat_IsNotAResultAndKeepsWaitingForTheRealOne(string badCode)
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State, ShortGrace);
        receiver.Start();
        var wait = receiver.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        // Act
        var bad = await GetAsync(receiver.Port, $"/kumi-login?code={badCode}&state={State}");
        var good = await GetAsync(receiver.Port, $"/kumi-login?code=Real-code_1&state={State}");
        var outcome = await wait;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bad, Does.StartWith("HTTP/1.1 400"));
            Assert.That(good, Does.StartWith("HTTP/1.1 303"));
            Assert.That(outcome.Code, Is.EqualTo("Real-code_1"));
        }
    }

    [Test]
    public async Task Receiver_ACodeThatIsTooLong_IsNotAResult()
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State);
        receiver.Start();
        var wait = receiver.WaitAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        // Act
        var page = await GetAsync(receiver.Port, $"/kumi-login?code={new string('a', 513)}&state={State}");
        var outcome = await wait;

        // Assert
        Assert.That(page, Does.StartWith("HTTP/1.1 400"));
        Assert.That(outcome.Kind, Is.EqualTo(LoopbackKind.TimedOut));
    }

    [Test]
    public async Task Receiver_MoreConnectionsThanTheLimit_AreClosedWithoutAnswerAndTheRealOneStillWorksOnceThereIsRoom()
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State, ShortGrace);
        receiver.Start();
        var wait = receiver.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        var stalled = new List<TcpClient>();
        try
        {
            for (var index = 0; index < LoopbackReceiver.MaxConcurrentConnections; index++)
            {
                var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", receiver.Port);
                stalled.Add(client);
            }

            await Task.Delay(300);

            // Act
            using var extra = new TcpClient();
            await extra.ConnectAsync("127.0.0.1", receiver.Port);
            var buffer = new byte[16];
            var read = await extra.GetStream().ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.That(read, Is.Zero, "超過上限的連線直接被關掉");
        }
        finally
        {
            foreach (var client in stalled)
            {
                client.Dispose();
            }
        }

        // 卡住的連線放掉之後，真正的回呼進得來。
        await Task.Delay(300);
        await GetAsync(receiver.Port, $"/kumi-login?code=real&state={State}");
        Assert.That((await wait).Code, Is.EqualTo("real"));
    }

    [Test]
    public async Task Receiver_AConnectionThatNeverFinishesItsHeaders_IsClosedAfterTheHeaderTimeoutFreeingTheSlot()
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State, ShortGrace, TimeSpan.FromMilliseconds(300));
        receiver.Start();
        var wait = receiver.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        var stalled = new List<TcpClient>();
        try
        {
            for (var index = 0; index < LoopbackReceiver.MaxConcurrentConnections; index++)
            {
                var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", receiver.Port);
                await client.GetStream().WriteAsync("GET /kumi-login HTTP/1.1\r\nHost: x"u8.ToArray());
                stalled.Add(client);
            }

            // Act（時限過後，卡住的連線被關掉、位子空出來）
            var closed = await stalled[0].GetStream().ReadAsync(new byte[16]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var page = await GetAsync(receiver.Port, $"/kumi-login?code=real&state={State}");

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(closed, Is.Zero);
                Assert.That(page, Does.StartWith("HTTP/1.1 303"));
                Assert.That((await wait).Code, Is.EqualTo("real"));
            }
        }
        finally
        {
            foreach (var client in stalled)
            {
                client.Dispose();
            }
        }
    }
}
