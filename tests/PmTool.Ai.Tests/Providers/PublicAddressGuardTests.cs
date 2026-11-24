using System.Net;
using System.Net.Sockets;
using System.Text;
using PmTool.Ai.Providers;
using PmTool.Http;

namespace PmTool.Ai.Tests.Providers;

/// <summary>相容端點的位址檢查：位址的判斷，與連線當下真的擋下來（走真的 SocketsHttpHandler，不是假的 handler）。</summary>
[TestFixture]
public sealed class PublicAddressGuardTests
{
    [TestCase("127.0.0.1", true)]
    [TestCase("127.8.8.8", true)]
    [TestCase("10.0.0.1", true)]
    [TestCase("10.255.255.255", true)]
    [TestCase("172.16.0.1", true)]
    [TestCase("172.31.255.255", true)]
    [TestCase("172.32.0.1", false)]
    [TestCase("172.15.255.255", false)]
    [TestCase("192.168.1.1", true)]
    [TestCase("192.169.1.1", false)]
    [TestCase("100.64.0.1", true)]
    [TestCase("100.127.255.255", true)]
    [TestCase("100.128.0.1", false)]
    [TestCase("169.254.169.254", true)]
    [TestCase("0.0.0.0", true)]
    [TestCase("224.0.0.1", true)]
    [TestCase("8.8.8.8", false)]
    [TestCase("93.184.216.34", false)]
    [TestCase("::1", true)]
    [TestCase("::", true)]
    [TestCase("fe80::1", true)]
    [TestCase("fc00::1", true)]
    [TestCase("fd12:3456::1", true)]
    [TestCase("ff02::1", true)]
    [TestCase("::ffff:10.0.0.1", true)]
    [TestCase("::ffff:127.0.0.1", true)]
    [TestCase("::ffff:8.8.8.8", false)]
    [TestCase("2606:4700:4700::1111", false)]
    // 保留給文件、測試與基準測試的 IPv4 網段。
    [TestCase("192.0.0.1", true)]
    [TestCase("192.0.1.1", false)]
    [TestCase("192.0.2.10", true)]
    [TestCase("198.17.255.255", false)]
    [TestCase("198.18.0.1", true)]
    [TestCase("198.19.255.255", true)]
    [TestCase("198.20.0.1", false)]
    [TestCase("198.51.100.7", true)]
    [TestCase("198.51.101.7", false)]
    [TestCase("203.0.113.9", true)]
    [TestCase("203.0.114.9", false)]
    [TestCase("240.0.0.1", true)]
    [TestCase("255.255.255.255", true)]
    // IPv6 裡內嵌 IPv4 的形式：取出內嵌的位址再判斷。
    [TestCase("64:ff9b::a00:1", true)]
    [TestCase("64:ff9b::7f00:1", true)]
    [TestCase("64:ff9b::a9fe:a9fe", true)]
    [TestCase("64:ff9b::808:808", false)]
    [TestCase("64:ff9b:1::1", true)]
    [TestCase("2002:a00:1::", true)]
    [TestCase("2002:7f00:1::1", true)]
    [TestCase("2002:a9fe:a9fe::1", true)]
    [TestCase("2002:808:808::1", false)]
    [TestCase("::a00:1", true)]
    [TestCase("::a9fe:a9fe", true)]
    [TestCase("::808:808", false)]
    [TestCase("2001:0:4136:e378:8000:63bf:3fff:fdd2", true)]
    [TestCase("2001:0:0:0:0:0:0:1", true)]
    [TestCase("2001:db8::1", true)]
    public void IsBlocked_ClassifiesAddresses(string text, bool expected)
    {
        // Arrange
        var address = IPAddress.Parse(text);

        // Act & Assert
        Assert.That(PublicAddressGuard.IsBlocked(address), Is.EqualTo(expected));
    }

    [TestCase("http://127.0.0.1:8080/v1", true)]
    [TestCase("http://[::1]:8080/v1", true)]
    [TestCase("http://169.254.169.254/latest", true)]
    [TestCase("http://ollama.example.com/v1", false)]
    [TestCase("https://8.8.8.8/v1", false)]
    public void IsBlockedLiteral_OnlyJudgesIpLiterals(string url, bool expected) =>
        Assert.That(PublicAddressGuard.IsBlockedLiteral(new Uri(url)), Is.EqualTo(expected));

    /// <summary>極簡的 HTTP 伺服器：記下連線數，每個連線回一個固定的回應。</summary>
    private sealed class LocalServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly string _response;
        private int _connections;

        public LocalServer(string response)
        {
            _response = response;
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int Connections => Volatile.Read(ref _connections);

        public void Dispose() => _listener.Stop();

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _connections);
                    _ = Task.Run(() => ServeAsync(client));
                }
            }
            catch (Exception exception) when (exception is ObjectDisposedException or SocketException or InvalidOperationException)
            {
                // 測試結束時關掉 listener，接受連線的迴圈就在這裡結束。
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[4096];
                _ = await stream.ReadAsync(buffer);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(_response));
            }
        }
    }

    private static string Ok(string body) =>
        $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

    private const string ModelsBody = """{"object":"list","data":[{"id":"llama3","object":"model","created":1,"owned_by":"x"}]}""";

    [TestCase("127.0.0.1")]
    [TestCase("localhost")]
    public async Task Discover_LoopbackByDefault_IsBlockedBeforeAnyConnectionIsMade(string host)
    {
        // Arrange
        using var server = new LocalServer(Ok(ModelsBody));
        using var gateway = new AiProviderGateway(new AiModuleOptions());

        // Act
        var failure = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, $"http://{host}:{server.Port}/v1", null), CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Message, Does.Contain("內網或本機").And.Contain("Ai:AllowPrivateEndpoints"));
            Assert.That(server.Connections, Is.Zero);
        }
    }

    [Test]
    public async Task Discover_CloudMetadataAddress_IsBlockedEvenThoughNothingListens()
    {
        // Arrange
        using var gateway = new AiProviderGateway(new AiModuleOptions());

        // Act
        var failure = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, "http://169.254.169.254/v1", null), CancellationToken.None));

        // Assert
        Assert.That(failure.Message, Does.StartWith("169.254.169.254 是內網或本機的位址"));
    }

    [Test]
    public async Task Discover_PrivateEndpointsAllowed_ConnectsAndReadsTheCatalog()
    {
        // Arrange
        using var server = new LocalServer(Ok(ModelsBody));
        using var gateway = new AiProviderGateway(new AiModuleOptions(AllowPrivateEndpoints: true));

        // Act
        var models = await gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, $"http://127.0.0.1:{server.Port}/v1", null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(models.Select(m => m.Id), Is.EqualTo(new[] { "llama3" }));
            Assert.That(server.Connections, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Discover_PersonalEndpoint_IsStillBlockedWhenThePrivateEndpointsAreAllowed()
    {
        // Arrange
        using var server = new LocalServer(Ok(ModelsBody));
        using var gateway = new AiProviderGateway(new AiModuleOptions(AllowPrivateEndpoints: true));

        // Act
        var failure = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, $"http://127.0.0.1:{server.Port}/v1", null, Personal: true), CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Message, Does.Contain("自己的 AI 帳號不能連內部網路的位址").And.Contain("組織的連線"));
            Assert.That(server.Connections, Is.Zero);
        }
    }

    [Test]
    public async Task CreateChatClient_PersonalEndpoint_IsStillBlockedWhenThePrivateEndpointsAreAllowed()
    {
        // Arrange
        using var server = new LocalServer(Ok(ModelsBody));
        using var gateway = new AiProviderGateway(new AiModuleOptions(AllowPrivateEndpoints: true));
        using var client = gateway.CreateChatClient(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, $"http://127.0.0.1:{server.Port}/v1", null, Personal: true), "llama3");

        // Act
        var thrown = Assert.CatchAsync(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hi")]))
            {
            }
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AiProviderFailures.Describe(thrown!, null, null, personal: true), Does.Contain("自己的 AI 帳號不能連內部網路的位址"));
            Assert.That(server.Connections, Is.Zero);
        }
    }

    [Test]
    public async Task Discover_Redirect_IsNotFollowedEvenToAnAllowedHost()
    {
        // Arrange
        using var target = new LocalServer(Ok(ModelsBody));
        var redirect = $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{target.Port}/v1/models\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        using var first = new LocalServer(redirect);
        using var gateway = new AiProviderGateway(new AiModuleOptions(AllowPrivateEndpoints: true));

        // Act
        var failure = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, $"http://127.0.0.1:{first.Port}/v1", null), CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Message, Does.Contain("302"));
            Assert.That(first.Connections, Is.EqualTo(1));
            Assert.That(target.Connections, Is.Zero);
        }
    }
}
