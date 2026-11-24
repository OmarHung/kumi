using System.Net;
using PmTool.Api.Endpoints;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint;

namespace PmTool.Api.Tests;

/// <summary>
/// 送出變更集的內文是文件本身，讀法和 /v1/changesets/validate 一樣：媒體類型、編碼、上限、讀取期限。
/// 和驗證端點不同的是要登入，而且不占驗證端點給匿名流量的位置。
/// </summary>
[TestFixture]
public sealed class ChangeSetProposalBodyTests
{
    private const string Route = "/v1/projects/WEB/changesets";

    // 註解與空行也要原樣交給模組：問題的行號是照收到的文件算的。
    private const string YamlDocument = """
        # 網頁的設定頁送出的
        baseVersion: 3
        summary: 拆開「待驗收」

        blueprint:
          apiVersion: blueprint/v0
        migrations:
          moveStates:
            - { workflow: main, from: review, to: internal_review }

        """;

    private const string JsonDocument = "{\n  \"baseVersion\": 3,\n  \"summary\": \"拆開「待驗收」\",\n  \"blueprint\": {}\n}\n";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [TestCase("application/json", JsonDocument, DocumentFormat.Json)]
    [TestCase("application/json; charset=utf-8", JsonDocument, DocumentFormat.Json)]
    [TestCase("application/yaml", YamlDocument, DocumentFormat.Yaml)]
    [TestCase("application/x-yaml", YamlDocument, DocumentFormat.Yaml)]
    [TestCase("text/yaml", YamlDocument, DocumentFormat.Yaml)]
    [TestCase("Application/YAML; charset=\"UTF-8\"", YamlDocument, DocumentFormat.Yaml)]
    public async Task Propose_SupportedContentType_PassesTheDocumentAsIsWithItsFormat(
        string contentType, string document, DocumentFormat format)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(document, contentType));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        var call = factory.Modules.Changes.ProposeCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.Document, Is.EqualTo(document));
            Assert.That(call.Format, Is.EqualTo(format));
        }
    }

    [TestCase("text/plain", ApiErrorCodes.UnsupportedMediaType)]
    [TestCase("application/xml", ApiErrorCodes.UnsupportedMediaType)]
    [TestCase("multipart/form-data; boundary=x", ApiErrorCodes.UnsupportedMediaType)]
    [TestCase(null, ApiErrorCodes.UnsupportedMediaType)]
    [TestCase("application/json; charset=utf-16", ApiErrorCodes.UnsupportedCharset)]
    [TestCase("application/yaml; charset=big5", ApiErrorCodes.UnsupportedCharset)]
    public async Task Propose_UnsupportedContentTypeOrCharset_Returns415WithoutReachingTheModule(
        string? contentType, string expectedCode)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(JsonDocument, contentType));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, expectedCode);
        Assert.That(factory.Modules.Changes.ProposeCalls, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Propose_BodyOverTheDocumentLimit_Returns413WithoutReachingTheModule(bool chunked)
    {
        // Arrange：有 Content-Length 的在讀之前就擋下；chunked 的讀到超過上限就停。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = Enumerable.Repeat((byte)'a', ApiLimits.MaxDocumentBytes + 1).ToArray();
        using var request = chunked
            ? ApiRequests.Chunked(Route, body)
            : new HttpRequestMessage(HttpMethod.Post, Route) { Content = ApiRequests.Bytes(body) };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge);
        Assert.That(factory.Modules.Changes.ProposeCalls, Is.Empty);
    }

    [Test]
    public async Task Propose_BodyExactlyAtTheDocumentLimit_IsPassedToTheModule()
    {
        // Arrange：比一般的 JSON 內文上限大，驗證過的文件送出時不會被另一個上限擋下。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = Enumerable.Repeat((byte)'a', ApiLimits.MaxDocumentBytes).ToArray();

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Bytes(body, "application/yaml"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(factory.Modules.Changes.ProposeCalls.Single().Document, Has.Length.EqualTo(ApiLimits.MaxDocumentBytes));
    }

    [Test]
    public async Task Propose_InvalidUtf8Bytes_Returns400WithoutReachingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        byte[] body = [(byte)'{', 0xC3, 0x28, (byte)'}'];

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Bytes(body));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.InvalidEncoding);
        Assert.That(factory.Modules.Changes.ProposeCalls, Is.Empty);
    }

    [Test]
    public async Task Propose_BodyStallsPastTheReadTimeout_Returns408WithoutReachingTheModule()
    {
        // Arrange：登入的請求照常讀，之後的內文才卡住。
        var stalled = new StalledBodies();
        var stall = false;
        var options = new ApiFactoryOptions
        {
            LoadLimits = new ValidationLoadLimits { BodyReadTimeout = TimeSpan.FromMilliseconds(200) },
            WrapRequestBody = body => Volatile.Read(ref stall) ? stalled.CreateStream() : body,
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        Volatile.Write(ref stall, true);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(JsonDocument));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestTimeout, ApiErrorCodes.RequestTimeout);
        Assert.That(factory.Modules.Changes.ProposeCalls, Is.Empty);
    }

    [Test]
    public async Task Propose_EveryValidationSlotIsTaken_IsStillAccepted()
    {
        // Arrange：匿名的驗證請求占住唯一的位置，卡在讀內文。
        var stalled = new StalledBodies();
        var stall = false;
        var options = new ApiFactoryOptions
        {
            LoadLimits = new ValidationLoadLimits { MaxConcurrentRequests = 1, BodyReadTimeout = TimeSpan.FromMinutes(5) },
            WrapRequestBody = body => Volatile.Read(ref stall) ? stalled.CreateStream() : body,
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        using var anonymous = factory.CreateClient();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        Volatile.Write(ref stall, true);
        var validation = anonymous.PostAsync(ApiRequests.ChangeSetRoute, ApiRequests.Document(JsonDocument));
        await stalled.WaitForStalledRequestsAsync(1, Patience);
        Volatile.Write(ref stall, false);

        // Act
        using var proposed = await client.PostAsync(Route, ApiRequests.Document(JsonDocument));
        using var anotherValidation = await anonymous.PostAsync(ApiRequests.ChangeSetRoute, ApiRequests.Document(JsonDocument));
        stalled.Open();
        using var validated = await validation.WaitAsync(Patience);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposed.StatusCode, Is.EqualTo(HttpStatusCode.Created), "送出不占驗證端點的位置");
            Assert.That(anotherValidation.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable), "位置確實是滿的");
            Assert.That(validated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }
}
