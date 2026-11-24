using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Sockets;
using PmTool.Ai.Providers;
using PmTool.Http;

namespace PmTool.Ai.Tests.Providers;

/// <summary>失敗的說明（第 4.5 節）：我們自己的分類，後面可以接供應商的話，不記金鑰、不超過上限。</summary>
[TestFixture]
public sealed class AiProviderFailuresTests
{
    private static HttpRequestException WithStatus(HttpStatusCode status, string? message = null) => new(message ?? "x", null, status);

    [TestCase(HttpStatusCode.Unauthorized, "金鑰不對，或這把金鑰沒有權限。")]
    [TestCase(HttpStatusCode.Forbidden, "金鑰不對，或這把金鑰沒有權限。")]
    [TestCase(HttpStatusCode.InternalServerError, "供應商那邊出錯了，稍後再試。")]
    [TestCase(HttpStatusCode.BadGateway, "供應商那邊出錯了，稍後再試。")]
    [TestCase(HttpStatusCode.ServiceUnavailable, "供應商那邊出錯了，稍後再試。")]
    public void Describe_StatusWithAFixedSentence_IgnoresTheProvidersText(HttpStatusCode status, string expected) =>
        Assert.That(AiProviderFailures.Describe(WithStatus(status, "secret provider text sk-abcdef123456")), Is.EqualTo(expected));

    [Test]
    public void Describe_NotFound_RaisesTheModelOrBaseUrl()
    {
        // Act
        var text = AiProviderFailures.Describe(WithStatus(HttpStatusCode.NotFound, "The model `gpt-9` does not exist"));

        // Assert
        Assert.That(text, Does.StartWith("找不到（模型代號不對，或基底網址不對）").And.Contain("gpt-9"));
    }

    [Test]
    public void Describe_TooManyRequests_SaysToRetryLater()
    {
        // Act
        var text = AiProviderFailures.Describe(WithStatus((HttpStatusCode)429, "quota"));

        // Assert
        Assert.That(text, Does.StartWith("供應商說太頻繁或額度用完了，稍後再試"));
    }

    [Test]
    public void Describe_BadRequest_AppendsTheProvidersJsonMessage()
    {
        // Arrange
        var exception = new ClientResultException(new FakeResponse(400, """{"error":{"message":"Unsupported value: 'reasoning_effort' does not support 'xhigh' with this model."}}"""));

        // Act
        var text = AiProviderFailures.Describe(exception);

        // Assert
        Assert.That(text, Does.StartWith("供應商不接受這個請求（例如這個模型不支援這個思考深度）：").And.Contain("does not support 'xhigh'"));
    }

    [Test]
    public void Describe_OpenAiAuthFailure_NeverEchoesTheProvidersTextWhichMayHoldPartOfTheKey()
    {
        // Arrange
        var exception = new ClientResultException(new FakeResponse(401, """{"error":{"message":"Incorrect API key provided: sk-proj-abc***xyz"}}"""));

        // Act
        var text = AiProviderFailures.Describe(exception);

        // Assert
        Assert.That(text, Is.EqualTo("金鑰不對，或這把金鑰沒有權限。"));
    }

    [Test]
    public void Describe_ProviderTextWithAKeyShapeOrKeyParameter_IsRedacted()
    {
        // Arrange
        var exception = WithStatus(HttpStatusCode.BadRequest, "bad request for https://x.example.com/v1?key=AIzaSyA1B2C3D4E5F6&alt=sse using sk-abcdefghijklmnop and Bearer abcdef123456");

        // Act
        var text = AiProviderFailures.Describe(exception);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Not.Contain("AIzaSy"));
            Assert.That(text, Does.Not.Contain("sk-abcdefghijklmnop"));
            Assert.That(text, Does.Not.Contain("abcdef123456"));
            Assert.That(text, Does.Contain("***"));
        }
    }

    [Test]
    public void Describe_ControlCharactersAndLongText_AreCleanedAndCut()
    {
        // Arrange
        var exception = WithStatus(HttpStatusCode.BadRequest, "line1\r\nline2\0" + new string('x', 5000));

        // Act
        var text = AiProviderFailures.Describe(exception);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Any(char.IsControl), Is.False);
            Assert.That(text.Length, Is.LessThanOrEqualTo(AiRules.MaxErrorLength));
            Assert.That(text, Does.Contain("line1 line2"));
        }
    }

    [Test]
    public void Describe_ConnectionFailure_NamesTheHostWhenKnown()
    {
        // Arrange
        var exception = new HttpRequestException("failed", new SocketException((int)SocketError.HostNotFound));

        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AiProviderFailures.Describe(exception, "ollama.example.com"), Is.EqualTo("連不上 ollama.example.com。"));
            Assert.That(AiProviderFailures.Describe(exception), Is.EqualTo("連不上供應商。"));
        }
    }

    [Test]
    public void Describe_BlockedAddressDeepInTheChain_SaysTheDeploymentDoesNotAllowIt()
    {
        // Arrange
        var exception = new HttpRequestException("outer", new BlockedAddressException("ollama.internal"));

        // Act
        var text = AiProviderFailures.Describe(exception);

        // Assert
        Assert.That(text, Is.EqualTo("ollama.internal 是內網或本機的位址，這個部署不允許連（Ai:AllowPrivateEndpoints）。"));
    }

    [Test]
    public void Describe_Timeouts_SayTheProviderTookTooLong()
    {
        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AiProviderFailures.Describe(new TaskCanceledException("t", new TimeoutException())), Is.EqualTo("供應商太久沒有回應。"));
            Assert.That(AiProviderFailures.Describe(new TimeoutException()), Is.EqualTo("供應商太久沒有回應。"));
        }
    }

    [Test]
    public void Describe_OurOwnProviderException_KeepsItsMessage() =>
        Assert.That(AiProviderFailures.Describe(new AiProviderException("供應商太久沒有回應。")), Is.EqualTo("供應商太久沒有回應。"));

    [Test]
    public void Describe_UnknownException_SaysNothingAboutItsContents() =>
        Assert.That(AiProviderFailures.Describe(new InvalidOperationException("secret detail sk-abcdefghijklmnop")), Is.EqualTo("呼叫供應商時出錯了。"));

    private sealed class FakeResponse(int status, string content) : PipelineResponse
    {
        public override int Status => status;

        public override string ReasonPhrase => "x";

        public override Stream? ContentStream { get; set; }

        public override BinaryData Content => BinaryData.FromString(content);

        protected override PipelineResponseHeaders HeadersCore => new FakeHeaders();

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Content);

        public override void Dispose()
        {
        }
    }

    private sealed class FakeHeaders : PipelineResponseHeaders
    {
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();

        public override bool TryGetValue(string name, out string? value)
        {
            value = null;
            return false;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }
    }

    [Test]
    public void Describe_ProviderEchoesAKeyWithoutAKnownPrefix_MasksItWhenTheKeyIsGiven()
    {
        // Arrange：相容端點自訂的 token 沒有 sk- 這類前綴，樣式遮不到。
        const string key = "tok_9f8e7d6c5b4a";
        var exception = WithStatus(HttpStatusCode.BadRequest, $"bad request for credential {key} on model x");

        // Act
        var masked = AiProviderFailures.Describe(exception, host: null, secret: key);
        var unmasked = AiProviderFailures.Describe(exception);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(masked, Does.Not.Contain(key).And.Contain("***"));
            Assert.That(unmasked, Does.Contain(key));
        }
    }

    [Test]
    public void Describe_KeyAtTheCutPoint_IsMaskedBeforeTheSnippetIsTruncated()
    {
        // Arrange：金鑰剛好跨過截斷的位置；先截斷再遮的話只剩前半段、比對不到。
        const string key = "tok_9f8e7d6c5b4a3210";
        var exception = WithStatus(HttpStatusCode.BadRequest, new string('x', 290) + key + " tail");

        // Act
        var text = AiProviderFailures.Describe(exception, host: null, secret: key);

        // Assert
        Assert.That(text, Does.Not.Contain("tok_"));
    }

    [Test]
    public void Describe_MessageCutInsideASurrogatePair_LeavesNoLoneSurrogate()
    {
        // Arrange：第 MaxErrorLength - 1 個字元是 emoji 的前半，切在那裡會留下孤立的高代理。
        var message = new string('錯', AiRules.MaxErrorLength - 20) + "😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀";
        var exception = new AiProviderException(message);

        // Act
        var text = AiProviderFailures.Describe(exception);

        // Assert
        Assert.That(HasLoneSurrogate(text), Is.False);
        Assert.That(text.Length, Is.LessThanOrEqualTo(AiRules.MaxErrorLength));
    }

    internal static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }
}
