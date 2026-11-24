using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;

namespace PmTool.Cli.Tests;

/// <summary>
/// 伺服器限流（429）時照 Retry-After 等一下再送：monday 匯入一次要送幾千個請求，超過每分鐘的額度是常態，不能整個失敗。
/// 限流是在處理之前就擋下的，重送不會重複寫入。
/// </summary>
[TestFixture]
public sealed class RateLimitRetryTests
{
    /// <summary>前 <paramref name="limited"/> 次回 429（帶 Retry-After），之後成功；記下每次收到的內容。</summary>
    private sealed class LimitedHandler(int limited, string? retryAfter = "3") : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            if (Bodies.Count <= limited)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("""{"success":false,"data":null,"error":{"code":"request.rate_limited","message":"請求太頻繁，請稍後再試。"}}"""),
                };
                if (retryAfter is not null)
                {
                    response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
                }

                return response;
            }

            return FakeServer.Envelope(HttpStatusCode.OK, new { ok = true });
        }
    }

    private static (ApiClient Api, List<TimeSpan> Waits) Client(HttpMessageHandler handler)
    {
        var waits = new List<TimeSpan>();
        var http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        var api = new ApiClient(http, "https://pm.example.com", "pmt_x")
        {
            Wait = (delay, _) =>
            {
                waits.Add(delay);
                return Task.CompletedTask;
            },
        };
        return (api, waits);
    }

    [Test]
    public async Task RateLimited_WaitsAsLongAsRetryAfterSays_AndResendsTheSameBody()
    {
        // Arrange
        var handler = new LimitedHandler(limited: 2);
        var (api, waits) = Client(handler);

        // Act
        var data = await api.PostAsync("things", new JsonObject { ["name"] = "example" }, CancellationToken.None);

        // Assert
        Assert.That(data.GetProperty("ok").GetBoolean(), Is.True);
        Assert.That(waits, Is.EqualTo(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3) }));
        Assert.That(handler.Bodies, Has.Count.EqualTo(3).And.All.EqualTo("""{"name":"example"}"""));
    }

    [Test]
    public async Task RateLimited_FileUpload_ResendsTheWholeFile()
    {
        // Arrange
        var handler = new LimitedHandler(limited: 1);
        var (api, _) = Client(handler);
        var file = new MemoryStream(Encoding.UTF8.GetBytes("example file content"));

        // Act
        await api.PostFormFileAsync("items/WEB-1/history/attachments", [("source", "monday")], file, "a.txt", "text/plain", CancellationToken.None);

        // Assert
        Assert.That(handler.Bodies, Has.Count.EqualTo(2));
        Assert.That(handler.Bodies[1], Does.Contain("example file content").And.Contain("monday"));
        // 每次重送是新的 multipart，分隔線不同；去掉分隔線之後內容一樣
        static string WithoutBoundary(string body) => System.Text.RegularExpressions.Regex.Replace(body, "--[0-9a-f-]{36}", "--boundary");
        Assert.That(WithoutBoundary(handler.Bodies[1]), Is.EqualTo(WithoutBoundary(handler.Bodies[0])));
    }

    [Test]
    public async Task RateLimited_WithoutRetryAfter_WaitsOneSecond_AndCapsLongWaitsAtAMinute()
    {
        // Arrange
        var (noHeader, waitsWithoutHeader) = Client(new LimitedHandler(limited: 1, retryAfter: null));
        var (longWait, waitsLong) = Client(new LimitedHandler(limited: 1, retryAfter: "3600"));

        // Act
        await noHeader.GetAsync("things", null, CancellationToken.None);
        await longWait.GetAsync("things", null, CancellationToken.None);

        // Assert
        Assert.That(waitsWithoutHeader, Is.EqualTo(new[] { TimeSpan.FromSeconds(1) }));
        Assert.That(waitsLong, Is.EqualTo(new[] { TimeSpan.FromSeconds(60) }));
    }

    [Test]
    public void RateLimited_EveryTime_GivesUpAfterTheRetryLimit()
    {
        // Arrange
        var handler = new LimitedHandler(limited: int.MaxValue);
        var (api, waits) = Client(handler);

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() => api.GetAsync("things", null, CancellationToken.None));

        // Assert
        Assert.That(failure!.Message, Does.Contain("請求太頻繁"));
        Assert.That(waits, Has.Count.EqualTo(10));
        Assert.That(handler.Bodies, Has.Count.EqualTo(11));
    }

    [Test]
    public void OtherErrors_AreNotRetried()
    {
        // Arrange
        var handler = new FailingHandler();
        var (api, waits) = Client(handler);

        // Act
        Assert.ThrowsAsync<CliException>(() => api.GetAsync("things", null, CancellationToken.None));

        // Assert
        Assert.That(waits, Is.Empty);
        Assert.That(handler.Calls, Is.EqualTo(1));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("oops") });
        }
    }
}
