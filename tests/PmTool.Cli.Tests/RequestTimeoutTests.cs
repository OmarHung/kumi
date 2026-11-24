using System.Net;
using PmTool.Cli.Api;

namespace PmTool.Cli.Tests;

/// <summary>逾時：一般請求 100 秒，上傳依檔案大小放寬（慢的上行傳 25 MiB 不能被一般的時限切掉）。</summary>
[TestFixture]
public sealed class RequestTimeoutTests
{
    private sealed class SlowHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                await request.Content.ReadAsStringAsync(cancellationToken);
            }

            await Task.Delay(delay, cancellationToken);
            return FakeServer.Envelope(HttpStatusCode.OK, new { ok = true });
        }
    }

    [Test]
    public void RealClient_HasNoOverallTimeoutAndUploadsGetMoreTimeThanOtherRequests()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HttpSettings.ClientTimeout, Is.EqualTo(Timeout.InfiniteTimeSpan));
            Assert.That(HttpSettings.UploadTimeout(0), Is.EqualTo(HttpSettings.Timeout));
            Assert.That(HttpSettings.UploadTimeout(25L * 1024 * 1024), Is.GreaterThan(TimeSpan.FromMinutes(5)));
        }
    }

    [Test]
    public void OrdinaryRequest_ThatOutlastsTheRequestTimeout_FailsWithATimeoutMessage()
    {
        // Arrange
        using var http = new HttpClient(new SlowHandler(TimeSpan.FromSeconds(5)), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        var api = new ApiClient(http, "https://pm.example.com", "pmt_x") { RequestTimeout = TimeSpan.FromMilliseconds(100) };

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() => api.GetAsync("projects", null, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(failure.Message, Does.Contain("沒有在時限內回應"));
        }
    }

    [Test]
    public async Task Upload_ThatOutlastsTheOrdinaryTimeout_StillSucceedsWithinTheUploadTimeout()
    {
        // Arrange
        using var http = new HttpClient(new SlowHandler(TimeSpan.FromMilliseconds(500)), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        var api = new ApiClient(http, "https://pm.example.com", "pmt_x")
        {
            RequestTimeout = TimeSpan.FromMilliseconds(100),
            UploadTimeout = _ => TimeSpan.FromSeconds(10),
        };

        // Act
        var data = await api.PostFileAsync("items/WEB-1/attachments", new MemoryStream([1, 2, 3]), "a.bin", "application/octet-stream", CancellationToken.None);

        // Assert
        Assert.That(data.GetProperty("ok").GetBoolean(), Is.True);
    }

    [Test]
    public void Upload_ThatOutlastsTheUploadTimeout_FailsWithATimeoutMessage()
    {
        // Arrange
        using var http = new HttpClient(new SlowHandler(TimeSpan.FromSeconds(5)), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        var api = new ApiClient(http, "https://pm.example.com", "pmt_x") { UploadTimeout = _ => TimeSpan.FromMilliseconds(100) };

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() =>
            api.PostFileAsync("items/WEB-1/attachments", new MemoryStream([1]), "a.bin", "application/octet-stream", CancellationToken.None));

        // Assert
        Assert.That(failure!.Message, Does.Contain("沒有在時限內回應"));
    }

    [Test]
    public void CallerCancellation_IsStillACancellationNotATimeout()
    {
        // Arrange
        using var http = new HttpClient(new SlowHandler(TimeSpan.FromSeconds(5)), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        var api = new ApiClient(http, "https://pm.example.com", "pmt_x");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        Assert.CatchAsync<OperationCanceledException>(() => api.GetAsync("projects", null, cancel.Token));
    }
}
