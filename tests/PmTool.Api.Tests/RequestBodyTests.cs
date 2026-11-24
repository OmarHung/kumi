using System.Net;
using Microsoft.AspNetCore.Http;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class RequestBodyTests
{
    // 內文是整塊整塊從連線讀進來的，判斷出「超過上限」時可能已經多讀了一小段。
    // 重點是不能把後面的內文也讀完，所以只要遠小於一份文件的上限就好，確切的數字是實作細節。
    private const int ReadAheadAllowance = 64 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    [TestCase("text/plain")]
    [TestCase("application/xml")]
    [TestCase("application/json-patch+json")]
    [TestCase("application/yaml-x")]
    [TestCase("not a media type")]
    public async Task Validate_UnsupportedContentType_Returns415(string contentType)
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}", contentType));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType);
        Assert.That(validator.Calls, Is.Empty);
    }

    [Test]
    public async Task Validate_MissingContentType_Returns415()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.ChangeSetRoute, ApiRequests.Document("{}", contentType: null));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType);
    }

    [TestCase("application/json; charset=utf-16")]
    [TestCase("application/yaml; charset=iso-8859-1")]
    [TestCase("text/yaml; charset=big5")]
    public async Task Validate_NonUtf8Charset_Returns415WithCharsetCode(string contentType)
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}", contentType));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedCharset);
        Assert.That(validator.Calls, Is.Empty);
    }

    [Test]
    public async Task Validate_ContentLengthOverLimit_Returns413WithoutReadingBody()
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        CountingStream? counter = null;
        var options = new ApiFactoryOptions { WrapRequestBody = body => counter = new CountingStream(body) };
        using var factory = new ApiFactory(validator, options);
        using var client = factory.CreateClient();
        client.Timeout = RequestTimeout;
        var body = new byte[ApiLimits.MaxDocumentBytes + 1];

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Bytes(body));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validator.Calls, Is.Empty);
            Assert.That(counter?.BytesRead, Is.Zero);
        }
    }

    [Test]
    public async Task Validate_UnknownLengthBodyOverLimit_Returns413WithoutReadingTheRestOfTheBody()
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        CountingStream? counter = null;
        var options = new ApiFactoryOptions { WrapRequestBody = body => counter = new CountingStream(body) };
        using var factory = new ApiFactory(validator, options);
        using var client = factory.CreateClient();
        client.Timeout = RequestTimeout;
        using var request = ApiRequests.Chunked(ApiRequests.BlueprintRoute, new byte[ApiLimits.MaxDocumentBytes * 3]);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validator.Calls, Is.Empty);
            Assert.That(counter?.BytesRead, Is.InRange(
                ApiLimits.MaxDocumentBytes + 1L, ApiLimits.MaxDocumentBytes + (long)ReadAheadAllowance));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Validate_BodyExactlyAtLimit_IsAccepted(bool chunked)
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();
        client.Timeout = RequestTimeout;
        var body = Enumerable.Repeat((byte)'a', ApiLimits.MaxDocumentBytes).ToArray();
        using var request = chunked
            ? ApiRequests.Chunked(ApiRequests.BlueprintRoute, body)
            : new HttpRequestMessage(HttpMethod.Post, ApiRequests.BlueprintRoute) { Content = ApiRequests.Bytes(body) };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(validator.Calls.Single().Text?.Length, Is.EqualTo(ApiLimits.MaxDocumentBytes));
        }
    }

    [Test]
    public async Task Validate_InvalidUtf8Bytes_Returns400()
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();
        byte[] body = [(byte)'{', 0xC3, 0x28, (byte)'}'];

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Bytes(body));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.InvalidEncoding);
        Assert.That(validator.Calls, Is.Empty);
    }

    [TestCase(StatusCodes.Status400BadRequest, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest)]
    [TestCase(StatusCodes.Status413PayloadTooLarge, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge)]
    [TestCase(StatusCodes.Status408RequestTimeout, HttpStatusCode.RequestTimeout, ApiErrorCodes.RequestTimeout)]
    public async Task Validate_ServerRejectsBodyWhileReading_ReturnsMatchingEnvelope(
        int serverStatus, HttpStatusCode expectedStatus, string expectedCode)
    {
        // Arrange
        var options = new ApiFactoryOptions
        {
            WrapRequestBody = _ => new ThrowingStream(new BadHttpRequestException("內文格式錯誤", serverStatus)),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expectedStatus, expectedCode);
    }
}
