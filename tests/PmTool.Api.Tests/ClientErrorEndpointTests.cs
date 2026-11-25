using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Api.Endpoints;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>網頁回報的錯誤：照合約驗證，以 Warning 等級記在固定的類別底下，只記人員編號。</summary>
[TestFixture]
public sealed class ClientErrorEndpointTests
{
    private const string Route = "/v1/client-errors";
    private const string Category = "PmTool.Api.ClientErrors";

    [Test]
    public async Task Report_Valid_Returns200AndLogsAnErrorWithStructuredProperties()
    {
        // Arrange
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new { kind = "unhandled_rejection", message = "Example 失敗", stack = "at example (main.js:1:2)", path = "/projects/WEB" };

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(body));

        // Assert
        Assert.That(await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK), Is.Null);
        var entry = logs.Entries.Single(e => e.Category == Category);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Properties["Kind"], Is.EqualTo("unhandled_rejection"));
            Assert.That(entry.Properties["ErrorMessage"], Is.EqualTo("Example 失敗"));
            Assert.That(entry.Properties["Stack"], Is.EqualTo("at example (main.js:1:2)"));
            Assert.That(entry.Properties["Path"], Is.EqualTo("/projects/WEB"));
            Assert.That(entry.Properties["PersonId"], Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(entry.Message, Does.Contain("Example 失敗").And.Contain(TestUsers.Member.PersonId.ToString()));
        }
    }

    [Test]
    public async Task Report_Logs_NeitherTheNameNorTheEmailOfThePerson()
    {
        // Arrange：記錄會被複製、轉寄，裡面不該有個人資料。
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, Report());

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var entry = logs.Entries.Single(e => e.Category == Category);
        var everything = entry.Message + string.Join("|", entry.Properties.Values);
        Assert.That(everything, Does.Not.Contain(TestUsers.Member.DisplayName).And.Not.Contain(TestUsers.Member.Email));
    }

    [TestCase("""{"kind":"error","message":"m","path":"/"}""", TestName = "Report_WithoutStack_IsAccepted")]
    [TestCase("""{"kind":"render","message":"m","stack":null,"path":"/"}""", TestName = "Report_StackNull_IsAccepted")]
    public async Task Report_StackIsOptional(string body)
    {
        // Arrange
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(logs.Entries.Single(e => e.Category == Category).Properties["Stack"], Is.Null);
    }

    [Test]
    public async Task Report_ControlCharacters_AreReplacedWithSpacesBeforeLogging()
    {
        // Arrange：換行會讓一筆記錄看起來像好幾筆；這是診斷資料，不擋，換成空白照樣記。
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        const string body = """{"kind":"error","message":"a\nb\u0000c\u001bd","stack":"at x\r\nat y","path":"/a\tb"}""";

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var entry = logs.Entries.Single(e => e.Category == Category);
        Assert.That(entry.Properties["ErrorMessage"], Is.EqualTo("a b c d"));
        Assert.That(entry.Properties["Stack"], Is.EqualTo("at x  at y"));
        Assert.That(entry.Properties["Path"], Is.EqualTo("/a b"));
    }

    [Test]
    public async Task Report_FormatAndSeparatorCharacters_AreReplacedWithSpacesBeforeLogging()
    {
        // Arrange：雙向控制字元與行、段落分隔字元在 JSON 記錄裡沒事，但 errors.sh 解碼後印到終端機時，
        // 可以把訊息顯示成誤導的樣子（例如把後面的字倒過來排），所以和控制字元一樣換成空白。
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        const string body = """{"kind":"error","message":"a\u202Eb\u2066c\u200Bd","stack":"x\u2028y\u2029z","path":"/p\u202Dq"}""";

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var entry = logs.Entries.Single(e => e.Category == Category);
        Assert.That(entry.Properties["ErrorMessage"], Is.EqualTo("a b c d"));
        Assert.That(entry.Properties["Stack"], Is.EqualTo("x y z"));
        Assert.That(entry.Properties["Path"], Is.EqualTo("/p q"));
    }

    [TestCase("/projects/WEB?search=secret", TestName = "Report_PathWithAQuery_LogsOnlyThePath")]
    [TestCase("/projects/WEB#token", TestName = "Report_PathWithAFragment_LogsOnlyThePath")]
    public async Task Report_PathWithMoreThanThePath(string path)
    {
        // Arrange
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, Report(path: path));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(logs.Entries.Single(e => e.Category == Category).Properties["Path"], Is.EqualTo("/projects/WEB"));
    }

    [Test]
    public async Task Report_ValuesAtTheLimits_AreAccepted()
    {
        // Arrange
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = Report(
            message: new string('m', ApiLimits.MaxClientErrorMessageLength),
            stack: new string('s', ApiLimits.MaxClientErrorStackLength),
            path: "/" + new string('p', ApiLimits.MaxClientErrorPathLength - 1));

        // Act
        using var response = await client.PostAsync(Route, body);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(logs.Entries.Count(e => e.Category == Category), Is.EqualTo(1));
    }

    [TestCaseSource(nameof(InvalidReports))]
    public async Task Report_Invalid_Returns400WithThePathAndLogsNothing(string body, string path)
    {
        // Arrange
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.EqualTo(new[] { path }));
        Assert.That(logs.Entries.Where(e => e.Category == Category), Is.Empty);
    }

    [Test]
    public async Task Report_MoreThanTheLimitPerPerson_Returns429ForThatPersonOnly()
    {
        // Arrange：一個壞掉的畫面可能每秒丟好幾次例外，記錄寫在磁碟上，不能讓一個人把它灌滿。
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var member = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var owner = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var accepted = new List<HttpStatusCode>();
        for (var i = 0; i < ApiLimits.ClientErrorPermitsPerWindow; i++)
        {
            using var report = await member.PostAsync(Route, Report(message: $"失敗 {i}"));
            accepted.Add(report.StatusCode);
        }

        // Act
        using var rejected = await member.PostAsync(Route, Report());
        using var other = await owner.PostAsync(Route, Report());

        // Assert
        Assert.That(accepted, Is.All.EqualTo(HttpStatusCode.OK));
        await ApiRequests.AssertErrorEnvelopeAsync(rejected, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(logs.Entries.Count(e => e.Category == Category), Is.EqualTo(ApiLimits.ClientErrorPermitsPerWindow + 1));
    }

    [Test]
    public async Task Report_MoreThanTheLimitForEveryoneTogether_Returns429EvenForSomeoneWithBudgetLeft()
    {
        // Arrange：每人的額度擋不住很多人一起送（或一個人換很多帳號），記錄的輪替上限是固定的，
        // 所以全站另外有一個上限。測試把它調小，才不必登入幾十個人。
        var limits = new ClientErrorLimits { PermitsPerWindow = 3 };
        var (factory, logs) = CreateFactory(services => services.AddSingleton(limits));
        using var _ = factory;
        using var member = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var owner = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        using var first = await member.PostAsync(Route, Report(message: "失敗 1"));
        using var second = await member.PostAsync(Route, Report(message: "失敗 2"));
        using var third = await owner.PostAsync(Route, Report(message: "失敗 3"));

        // Act
        using var rejected = await owner.PostAsync(Route, Report(message: "失敗 4"));

        // Assert
        Assert.That(new[] { first.StatusCode, second.StatusCode, third.StatusCode }, Is.All.EqualTo(HttpStatusCode.OK));
        await ApiRequests.AssertErrorEnvelopeAsync(rejected, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        Assert.That(rejected.Headers.RetryAfter, Is.Not.Null);
        Assert.That(logs.Entries.Count(e => e.Category == Category), Is.EqualTo(3));
    }

    [Test]
    public async Task Report_SameKindAndMessageFromTheSamePersonWithinAMinute_IsAcceptedButLoggedOnce()
    {
        // Arrange
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var first = await client.PostAsync(Route, Report());
        using var repeated = await client.PostAsync(Route, Report(path: "/another"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(repeated.StatusCode, Is.EqualTo(HttpStatusCode.OK), "被略過的回應和成功的一樣");
            Assert.That(await repeated.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()));
            Assert.That(logs.Entries.Count(e => e.Category == Category), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Report_SameMessageAfterAMinute_IsLoggedAgain()
    {
        // Arrange
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var first = await client.PostAsync(Route, Report());

        // Act
        factory.Modules.Time.Advance(TimeSpan.FromSeconds(ApiLimits.RateLimitWindowSeconds + 1));
        using var second = await client.PostAsync(Route, Report());

        // Assert
        Assert.That(logs.Entries.Count(e => e.Category == Category), Is.EqualTo(2));
    }

    [Test]
    public async Task Report_SameMessageFromDifferentPeopleOrKinds_IsLoggedEachTime()
    {
        // Arrange
        var (factory, logs) = CreateFactory();
        using var _ = factory;
        using var member = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var owner = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var a = await member.PostAsync(Route, Report());
        using var b = await owner.PostAsync(Route, Report());
        using var c = await member.PostAsync(Route, Report(kind: "render"));

        // Assert
        Assert.That(logs.Entries.Count(e => e.Category == Category), Is.EqualTo(3));
    }

    [Test]
    public async Task Report_DuplicateDoesNotTakeFromTheSiteWideBudget()
    {
        // Arrange：全站只有 2 次；重複的不該占掉額度。
        var limits = new ClientErrorLimits { PermitsPerWindow = 2 };
        var (factory, logs) = CreateFactory(services => services.AddSingleton(limits));
        using var _ = factory;
        using var member = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var owner = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        using var first = await member.PostAsync(Route, Report());
        using var duplicate = await member.PostAsync(Route, Report());

        // Act
        using var other = await owner.PostAsync(Route, Report(message: "別的"));

        // Assert
        Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(logs.Entries.Count(e => e.Category == Category), Is.EqualTo(2));
    }

    private static (ApiFactory Factory, CapturingLoggerProvider Logs) CreateFactory(Action<IServiceCollection>? configure = null)
    {
        var logs = new CapturingLoggerProvider();
        var options = new ApiFactoryOptions
        {
            ConfigureServices = services =>
            {
                services.AddSingleton<ILoggerProvider>(logs);
                configure?.Invoke(services);
            },
        };
        return (new ApiFactory(FakeBlueprintValidator.Passing(), options), logs);
    }

    private static HttpContent Report(string kind = "error", string message = "Example 失敗", string? stack = null, string path = "/") =>
        ApiRequests.Json(new { kind, message, stack, path });

    private static IEnumerable<TestCaseData> InvalidReports()
    {
        // 落單的代理字元在讀 JSON 時就被擋下，和其他格式錯誤一樣。
        yield return Invalid("""{"kind":"error","message":"a\ud800b","path":"/"}""", "message", "訊息有落單的代理字元");
        yield return Invalid(Body(message: "\"\""), "message", "訊息是空的");
        yield return Invalid(Body(message: Quote(new string('m', ApiLimits.MaxClientErrorMessageLength + 1))), "message", "訊息太長");
        yield return Invalid(Body(stack: Quote(new string('s', ApiLimits.MaxClientErrorStackLength + 1))), "stack", "堆疊太長");
        yield return Invalid(Body(path: "\"projects\""), "path", "路徑不是 / 開頭");
        yield return Invalid(Body(path: Quote("/" + new string('p', ApiLimits.MaxClientErrorPathLength))), "path", "路徑太長");
        yield return Invalid(Body(kind: "\"warning\""), "kind", "不認得的種類");
        yield return Invalid(Body(kind: "\"Error\""), "kind", "種類的大小寫不對");
        yield return Invalid(Body(message: "42"), "message", "訊息不是字串");
        yield return Invalid("""{"kind":"error","path":"/"}""", string.Empty, "沒有訊息");
        yield return Invalid("""{"kind":"error","message":"m","path":"/","extra":1}""", "extra", "不認得的屬性");
    }

    private static string Body(string kind = "\"error\"", string message = "\"m\"", string stack = "null", string path = "\"/\"") =>
        $$"""{"kind":{{kind}},"message":{{message}},"stack":{{stack}},"path":{{path}}}""";

    private static string Quote(string text) => JsonSerializer.Serialize(text);

    private static TestCaseData Invalid(string body, string path, string name) => new TestCaseData(body, path).SetArgDisplayNames(name);
}
