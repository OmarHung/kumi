using System.Net;
using PmTool.Cli.Api;

namespace PmTool.Cli.Tests;

[TestFixture]
public sealed class ResponseLimitTests
{
    private sealed class BigHandler(bool declareLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content = declareLength
                ? new ByteArrayContent(new byte[ApiClient.MaxResponseBytes + 10])
                : new StreamingContent(ApiClient.MaxResponseBytes + 10);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    /// <summary>不宣告長度的回應（分段傳輸）：只能一邊讀一邊數。</summary>
    private sealed class StreamingContent(int length) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) => await stream.WriteAsync(new byte[length]);

        protected override bool TryComputeLength(out long computed)
        {
            computed = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(new byte[length]));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Get_WithAnOversizedResponse_FailsInsteadOfReadingItAll(bool declareLength)
    {
        // Arrange
        using var http = new HttpClient(new BigHandler(declareLength));
        var api = new ApiClient(http, "https://pm.example.com", CliHarness.Token);

        // Act
        var exception = Assert.ThrowsAsync<CliException>(async () => await api.GetAsync("session", null, CancellationToken.None));

        // Assert
        Assert.That(exception.Message, Does.Contain("超過 4 MB"));
    }
}
