using System.Net;
using System.Text;
using PmTool.Ai.Providers;
using PmTool.Ai.Settings;
using PmTool.Http;

namespace PmTool.Ai.Tests.Providers;

/// <summary>供應商回應的大小上限（AI-05）、自己的端點只能 https:443、自己的連線不回顯對方的話（AI-04）。</summary>
[TestFixture]
public sealed class AiProviderLimitsTests
{
    private static HttpClient ClientOver(HttpMessageHandler inner, long max) => new(new LimitedResponseHandler(inner, max), disposeHandler: false);

    private static HttpResponseMessage StreamOf(int bytes) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(new NonSeekableStream(bytes)),
    };

    [Test]
    public async Task Handler_BodyWithinTheLimit_PassesThroughWithItsHeaders()
    {
        // Arrange
        using var inner = new RecordingHandler(_ => RecordingHandler.Json("""{"ok":true}"""));
        using var http = ClientOver(inner, 1000);

        // Act
        using var response = await http.GetAsync("https://example.com/v1/models");
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Is.EqualTo("""{"ok":true}"""));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        }
    }

    [Test]
    public void Handler_DeclaredLengthOverTheLimit_FailsBeforeReading()
    {
        // Arrange
        using var inner = new RecordingHandler(_ => RecordingHandler.Json(new string('x', 100)));
        using var http = ClientOver(inner, 10);

        // Act
        var thrown = Assert.ThrowsAsync<AiProviderException>(() => http.GetAsync("https://example.com/v1/models"));

        // Assert
        Assert.That(thrown.Message, Is.EqualTo(LimitedResponseHandler.TooLargeMessage));
    }

    [Test]
    public async Task Handler_BufferedBodyWithoutALengthOverTheLimit_IsCutOff()
    {
        // Arrange：串流的回應沒有 Content-Length，要靠實際讀到的位元組。
        using var inner = new RecordingHandler(_ => StreamOf(100));
        using var http = ClientOver(inner, 10);

        // Act
        var thrown = Assert.ThrowsAsync<AiProviderException>(() => http.GetAsync("https://example.com/v1/models"));

        // Assert
        Assert.That(thrown.Message, Is.EqualTo(LimitedResponseHandler.TooLargeMessage));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Handler_StreamedBodyOverTheLimit_ThrowsOnTheReadThatCrossesIt()
    {
        // Arrange
        using var inner = new RecordingHandler(_ => StreamOf(100));
        using var http = ClientOver(inner, 40);
        using var response = await http.GetAsync("https://example.com/v1/chat", HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[30];

        // Act
        var first = await stream.ReadAtLeastAsync(buffer, buffer.Length);
        var second = Assert.ThrowsAsync<AiProviderException>(async () => await stream.ReadAtLeastAsync(buffer, buffer.Length));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(30));
            Assert.That(second.Message, Is.EqualTo(LimitedResponseHandler.TooLargeMessage));
        }
    }

    [Test]
    public async Task PersonalHandler_OnlyHttpsOn443Passes()
    {
        // Arrange
        using var inner = new RecordingHandler(_ => RecordingHandler.Json("{}"));
        using var http = new HttpClient(new PersonalEndpointHandler(inner), disposeHandler: false);

        // Act
        using var allowed = await http.GetAsync("https://example.com/v1/models");
        var plain = Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("http://example.com/v1/models"));
        var otherPort = Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://example.com:8443/v1/models"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(plain, Is.Not.Null);
            Assert.That(otherPort, Is.Not.Null);
            Assert.That(inner.Requests, Has.Count.EqualTo(1));
        }

        await Task.CompletedTask;
    }

    [Test]
    public void Gateway_PersonalCompatibleEndpointOnPlainHttp_IsRefusedWithTheSingleUnreachableSentence()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.Json("""{"data":[]}"""));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        var endpoint = new AiEndpoint(AiProviderKind.OpenaiCompatible, "http://example.com/v1", null, Personal: true);

        // Act
        var thrown = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(endpoint, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrown.Message, Is.EqualTo(AiProviderFailures.PersonalUnreachable));
            Assert.That(handler.Requests, Is.Empty);
        }
    }

    [Test]
    public void Gateway_PersonalEndpointErrorBody_IsNotEchoedButTheOrganizationsIs()
    {
        // Arrange
        const string leaked = "internal-secret-banner-9137";
        var handler = new RecordingHandler(_ => RecordingHandler.Json("{\"error\":{\"message\":\"" + leaked + "\"}}", HttpStatusCode.BadRequest));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);

        // Act
        var personal = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, "https://example.com/v1", null, Personal: true), CancellationToken.None));
        var organization = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, "https://example.com/v1", null, Personal: false), CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(personal.Message, Does.Not.Contain(leaked));
            Assert.That(organization.Message, Does.Contain(leaked));
        }
    }

    [Test]
    public void Failures_PersonalBlockedAddressAndPersonalUnreachable_AreTheSameSentence()
    {
        // Act
        var blocked = AiProviderFailures.Describe(new BlockedAddressException("db.internal.example"), host: null, personal: true);
        var refused = AiProviderFailures.Describe(new HttpRequestException("Connection refused"), host: "db.internal.example", personal: true);
        var socket = AiProviderFailures.Describe(new System.Net.Sockets.SocketException(), host: null, personal: true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blocked, Is.EqualTo(AiProviderFailures.PersonalUnreachable));
            Assert.That(refused, Is.EqualTo(blocked));
            Assert.That(socket, Is.EqualTo(blocked));
            Assert.That(blocked, Does.Not.Contain("db.internal.example"));
        }
    }

    [Test]
    public void Failures_OrganizationBlockedAddress_StillNamesTheHost()
    {
        // Act
        var text = AiProviderFailures.Describe(new BlockedAddressException("db.internal.example"), host: null, personal: false);

        // Assert
        Assert.That(text, Does.Contain("db.internal.example").And.Contain("Ai:AllowPrivateEndpoints"));
    }

    [TestCase("http://example.com/v1")]
    [TestCase("https://example.com:8443/v1")]
    [TestCase("https://example.com:80/v1")]
    public void Validator_PersonalEndpointThatIsNotHttpsOn443_IsRefusedAtTheBaseUrl(string url)
    {
        // Arrange
        var input = new AiConnectionInput("我的", AiProviderKind.OpenaiCompatible, url, null, [], []);

        // Act
        var result = AiConnectionValidator.Validate(input, existing: null, allowPrivateEndpoints: false, personal: true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
            Assert.That(result.Failure?.Details.Single().Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(result.Failure?.Details.Single().Path, Is.EqualTo("baseUrl"));
        }
    }

    [TestCase("https://example.com/v1")]
    [TestCase("https://example.com:443/v1")]
    public void Validator_PersonalEndpointHttpsOn443_IsAccepted(string url)
    {
        // Arrange
        var input = new AiConnectionInput("我的", AiProviderKind.OpenaiCompatible, url, null, [], []);

        // Act
        var result = AiConnectionValidator.Validate(input, existing: null, allowPrivateEndpoints: false, personal: true);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
    }

    [Test]
    public void Validator_OrganizationEndpointOnPlainHttpAndOtherPort_StaysAllowed()
    {
        // Arrange
        var input = new AiConnectionInput("組織", AiProviderKind.OpenaiCompatible, "http://ollama.example.com:11434/v1", null, [], []);

        // Act
        var result = AiConnectionValidator.Validate(input, existing: null, allowPrivateEndpoints: false, personal: false);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
    }

    // 串流的回應事先不知道長度：StreamContent 對可以 seek 的串流會自己算出 Content-Length，所以要用不能 seek 的。
    private sealed class NonSeekableStream(int bytes) : Stream
    {
        private int _left = bytes;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, _left);
            _left -= n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
