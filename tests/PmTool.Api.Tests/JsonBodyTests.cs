using System.Net;
using System.Text;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>JSON 端點的內文：大小上限、格式、型別、不認得的屬性。用開專案的端點代表全部。</summary>
[TestFixture]
public sealed class JsonBodyTests
{
    private const string Route = "/v1/projects";

    // {"key":"WEB","name":"<填充>","template":"t"} 除了填充以外的長度。
    private const int EnvelopeLength = 38;

    [Test]
    public async Task Body_ExactlyAtTheLimit_IsAccepted()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = ProjectBodyOfLength(ApiLimits.MaxJsonBodyBytes);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Encoding.UTF8.GetByteCount(body), Is.EqualTo(ApiLimits.MaxJsonBodyBytes));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Body_OneByteOverTheLimit_Returns413WithoutCallingTheModule(bool chunked)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var bytes = Encoding.UTF8.GetBytes(ProjectBodyOfLength(ApiLimits.MaxJsonBodyBytes + 1));
        using var request = chunked
            ? ApiRequests.Chunked(Route, bytes)
            : new HttpRequestMessage(HttpMethod.Post, Route) { Content = ApiRequests.Bytes(bytes) };

        // Act
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Does.Contain(ApiLimits.MaxJsonBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(factory.Modules.Projects.CreateCalls, Is.Empty);
        }
    }

    [TestCase("{\"key\":\"WEB\",", "", TestName = "Body_NotValidJson_Returns400")]
    [TestCase("", "", TestName = "Body_Empty_Returns400")]
    [TestCase("null", "", TestName = "Body_JsonNull_Returns400")]
    [TestCase("[]", "", TestName = "Body_NotAnObject_Returns400")]
    [TestCase("{\"key\":1,\"name\":\"Example\",\"template\":\"t\"}", "key", TestName = "Body_WrongType_Returns400WithThePath")]
    [TestCase("{\"key\":null,\"name\":\"Example\",\"template\":\"t\"}", "key", TestName = "Body_NullForARequiredString_Returns400")]
    [TestCase("{\"name\":\"Example\",\"template\":\"t\"}", "", TestName = "Body_MissingProperty_Returns400")]
    [TestCase("{\"key\":\"WEB\",\"name\":\"Example\",\"template\":\"t\",\"owner\":\"x\"}", "owner", TestName = "Body_UnknownProperty_Returns400WithThePath")]
    [TestCase("{\"Key\":\"WEB\",\"name\":\"Example\",\"template\":\"t\"}", "Key", TestName = "Body_PropertyNameInAnotherCase_Returns400")]
    [TestCase("{\"key\":\"WEB\",\"key\":\"APP\",\"name\":\"Example\",\"template\":\"t\"}", "key", TestName = "Body_DuplicateProperty_Returns400")]
    public async Task Body_Rejected(string body, string expectedPath)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.All.EqualTo(expectedPath));
            Assert.That(text, Does.Not.Contain("PmTool."), "不透露 .NET 的型別名稱");
            Assert.That(factory.Modules.Projects.CreateCalls, Is.Empty);
        }
    }

    [TestCase("text/plain", ApiErrorCodes.UnsupportedMediaType)]
    [TestCase("application/yaml", ApiErrorCodes.UnsupportedMediaType)]
    [TestCase(null, ApiErrorCodes.UnsupportedMediaType)]
    [TestCase("application/json; charset=utf-16", ApiErrorCodes.UnsupportedCharset)]
    public async Task Body_NotJsonMediaType_Returns415(string? contentType, string expectedCode)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = """{"key":"WEB","name":"Example","template":"t"}""";

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body, contentType));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, expectedCode);
        Assert.That(factory.Modules.Projects.CreateCalls, Is.Empty);
    }

    [Test]
    public async Task Body_JsonWithUtf8Charset_IsAccepted()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            Route, ApiRequests.Document("""{"key":"WEB","name":"Example","template":"t"}""", "application/json; charset=utf-8"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    private static string ProjectBodyOfLength(int length) =>
        $$"""{"key":"WEB","name":"{{new string('x', length - EnvelopeLength)}}","template":"t"}""";
}
