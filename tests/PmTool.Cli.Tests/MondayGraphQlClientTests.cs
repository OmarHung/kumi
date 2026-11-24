using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Import.Monday;

namespace PmTool.Cli.Tests;

/// <summary>monday 的 GraphQL 用戶端：標頭、分頁用的變數、限流與重試、錯誤訊息不帶權杖。</summary>
[TestFixture]
public sealed class MondayGraphQlClientTests
{
    private const string Token = "mon_example_token_value";
    private const string Url = "https://monday.example.test/v2";

    private static HttpResponseMessage Reply(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Data(string data, string complexity = """{ "before": 100, "after": 90, "query": 10, "reset_in_x_seconds": 20 }""") =>
        Reply(HttpStatusCode.OK, $$"""{ "data": { "complexity": {{complexity}}, {{data}} } }""");

    private static (MondayGraphQlClient Client, List<TimeSpan> Waits, FakeServer Server) Create()
    {
        var server = new FakeServer();
        var waits = new List<TimeSpan>();
        var http = new HttpClient(server);
        var client = new MondayGraphQlClient(http, Url, Token, (span, _) =>
        {
            waits.Add(span);
            return Task.CompletedTask;
        });
        return (client, waits, server);
    }

    [Test]
    public async Task Query_SendsRawTokenApiVersionAndVariables_AndReturnsDataWithoutComplexity()
    {
        // Arrange
        var (client, _, server) = Create();
        server.On("POST", "/v2", _ => Data(""" "boards": [ { "id": "1" } ] """));

        // Act
        var data = await client.QueryAsync("$ids: [ID!]", "boards(ids: $ids) { id }", new JsonObject { ["ids"] = new JsonArray("1") }, CancellationToken.None);

        // Assert
        var request = server.Requests.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Header("Authorization"), Is.EqualTo(Token));
            Assert.That(request.Header("API-Version"), Is.EqualTo("2026-10"));
            Assert.That(request.Json!["query"]!.GetValue<string>(), Does.Contain("complexity { before after query reset_in_x_seconds }").And.Contain("boards(ids: $ids) { id }"));
            Assert.That(request.Json!["query"]!.GetValue<string>(), Does.StartWith("query($ids: [ID!])"));
            Assert.That(request.Json!["variables"]!["ids"]![0]!.GetValue<string>(), Is.EqualTo("1"));
            Assert.That(data.ContainsKey("complexity"), Is.False);
            Assert.That(data["boards"]![0]!["id"]!.GetValue<string>(), Is.EqualTo("1"));
        }
    }

    [Test]
    public void FromEnvironment_NoToken_SaysHowToSetIt()
    {
        // Arrange
        using var http = new HttpClient();
        var context = new CliContext { Http = http, Out = TextWriter.Null, Error = TextWriter.Null, In = TextReader.Null, ConfigPath = "x" };

        // Act
        var failure = Assert.Throws<CliException>(() => MondayGraphQlClient.FromEnvironment(context));

        // Assert
        Assert.That(failure!.Message, Does.Contain("MONDAY_API_TOKEN").And.Contain("export"));
    }

    [Test]
    public void FromEnvironment_UrlOverrideMustBeHttps()
    {
        // Arrange
        using var http = new HttpClient();
        var context = new CliContext
        {
            Http = http, Out = TextWriter.Null, Error = TextWriter.Null, In = TextReader.Null, ConfigPath = "x",
            GetEnvironmentVariable = name => name switch { "MONDAY_API_TOKEN" => Token, "MONDAY_API_URL" => "http://monday.example.test/v2", _ => null },
        };

        // Act
        var failure = Assert.Throws<CliException>(() => MondayGraphQlClient.FromEnvironment(context));

        // Assert
        Assert.That(failure!.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(failure.Message, Does.Contain("https").And.Not.Contain(Token));
    }

    [Test]
    public async Task Query_Http429WithRetryAfter_WaitsThatLongThenSucceeds()
    {
        // Arrange
        var (client, waits, server) = Create();
        var calls = 0;
        server.On("POST", "/v2", _ =>
        {
            if (++calls > 1)
            {
                return Data(""" "ok": 1 """);
            }

            var limited = Reply(HttpStatusCode.TooManyRequests, "{}");
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return limited;
        });

        // Act
        var data = await client.QueryAsync(string.Empty, "ok", null, CancellationToken.None);

        // Assert
        Assert.That(waits, Is.EqualTo(new[] { TimeSpan.FromSeconds(7) }));
        Assert.That(data["ok"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public async Task Query_ComplexityExceptionWithRetryInSeconds_WaitsThatLong()
    {
        // Arrange
        var (client, waits, server) = Create();
        var calls = 0;
        server.On("POST", "/v2", _ => ++calls == 1
            ? Reply(HttpStatusCode.OK, """{ "errors": [ { "message": "Complexity budget exhausted", "extensions": { "code": "ComplexityException", "retry_in_seconds": 12 } } ] }""")
            : Data(""" "ok": 1 """));

        // Act
        await client.QueryAsync(string.Empty, "ok", null, CancellationToken.None);

        // Assert
        Assert.That(waits, Is.EqualTo(new[] { TimeSpan.FromSeconds(12) }));
    }

    [Test]
    public async Task Query_RateLimitMessageWithoutSeconds_BacksOffExponentially()
    {
        // Arrange
        var (client, waits, server) = Create();
        var calls = 0;
        server.On("POST", "/v2", _ => ++calls <= 3
            ? Reply(HttpStatusCode.OK, """{ "errors": [ { "message": "Rate limit exceeded" } ] }""")
            : Data(""" "ok": 1 """));

        // Act
        await client.QueryAsync(string.Empty, "ok", null, CancellationToken.None);

        // Assert
        Assert.That(waits, Is.EqualTo(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8) }));
    }

    [Test]
    public void Query_StillLimitedAfterFiveRetries_GivesUp()
    {
        // Arrange
        var (client, waits, server) = Create();
        server.On("POST", "/v2", _ => Reply(HttpStatusCode.TooManyRequests, "{}"));

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() => client.QueryAsync(string.Empty, "ok", null, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(server.Requests, Has.Count.EqualTo(1 + MondayGraphQlClient.MaxRetries));
            Assert.That(waits, Has.Count.EqualTo(MondayGraphQlClient.MaxRetries));
            Assert.That(failure!.Message, Does.Contain("限流"));
        }
    }

    [Test]
    public async Task Query_BudgetLeftIsLessThanTheQueryCost_WaitsForResetBeforeTheNextRequest()
    {
        // Arrange
        var (client, waits, server) = Create();
        server.On("POST", "/v2", _ => Data(""" "ok": 1 """, """{ "before": 1000, "after": 50, "query": 950, "reset_in_x_seconds": 25 }"""));

        // Act
        await client.QueryAsync(string.Empty, "ok", null, CancellationToken.None);
        var waitedBeforeSecond = waits.Count;
        await client.QueryAsync(string.Empty, "ok", null, CancellationToken.None);

        // Assert
        Assert.That(waitedBeforeSecond, Is.Zero);
        Assert.That(waits, Is.EqualTo(new[] { TimeSpan.FromSeconds(26) }));
    }

    [Test]
    public void Query_ErrorMessageContainingTheToken_IsMasked()
    {
        // Arrange
        var (client, _, server) = Create();
        server.On("POST", "/v2", _ => Reply(HttpStatusCode.OK, $$"""{ "errors": [ { "message": "Bad field near {{Token}}" } ] }"""));

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() => client.QueryAsync(string.Empty, "ok", null, CancellationToken.None));

        // Assert
        Assert.That(failure!.Message, Does.Contain("Bad field").And.Not.Contain(Token));
    }

    [Test]
    public void Query_ConnectionFailureMentioningTheToken_IsMasked()
    {
        // Arrange
        var (client, _, server) = Create();
        server.Failure = new HttpRequestException($"failed {Token}");

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() => client.QueryAsync(string.Empty, "ok", null, CancellationToken.None));

        // Assert
        Assert.That(failure!.Message, Does.Contain("連不上 monday").And.Not.Contain(Token));
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public void Query_Unauthorized_SaysTheTokenIsRejectedWithoutRetrying(HttpStatusCode status)
    {
        // Arrange
        var (client, waits, server) = Create();
        server.On("POST", "/v2", _ => Reply(status, "{}"));

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() => client.QueryAsync(string.Empty, "ok", null, CancellationToken.None));

        // Assert
        Assert.That(failure!.Message, Does.Contain("權杖").And.Not.Contain(Token));
        Assert.That(waits, Is.Empty);
    }

    [Test]
    public void Query_OtherGraphQlError_FailsWithMondaysMessage()
    {
        // Arrange
        var (client, waits, server) = Create();
        server.On("POST", "/v2", _ => Reply(HttpStatusCode.OK, """{ "errors": [ { "message": "Field 'nope' doesn't exist" } ] }"""));

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() => client.QueryAsync(string.Empty, "nope", null, CancellationToken.None));

        // Assert
        Assert.That(failure!.Message, Does.Contain("Field 'nope' doesn't exist"));
        Assert.That(waits, Is.Empty);
    }

    [Test]
    public void Query_NotJson_FailsClearly()
    {
        // Arrange
        var (client, _, server) = Create();
        server.On("POST", "/v2", _ => Reply(HttpStatusCode.OK, "<html>oops</html>"));

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() => client.QueryAsync(string.Empty, "ok", null, CancellationToken.None));

        // Assert
        Assert.That(failure!.Message, Does.Contain("不是 JSON"));
    }
}
