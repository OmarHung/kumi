using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Knowledge;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>頁面附件的三個端點，對假的附件服務：形狀、上傳的讀法、失敗的對應、下載的標頭。</summary>
[TestFixture]
public sealed class KnowledgeAttachmentEndpointTests
{
    private const string PageRoute = "/v1/knowledge/spaces/HR/pages/2/attachments";

    private static string DownloadRoute(Guid? id = null) => $"/v1/knowledge/attachments/{id ?? KnowledgeSamples.AttachmentId}/content";

    [Test]
    public async Task List_ReturnsTheContractShapeWithTheUploaderName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(PageRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            $$$"""
            [{"id":"{{{KnowledgeSamples.AttachmentId}}}","pageId":"{{{KnowledgeSamples.PageId}}}","fileName":"規格.pdf","contentType":"application/pdf","sizeBytes":11,
              "sha256":"b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
              "uploadedBy":{"personId":"{{{TestUsers.Member.PersonId}}}","displayName":"Member Example"},"uploadedAt":"2026-11-20T03:00:00Z"}]
            """);
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Upload_AMultipartFile_PassesSpacePageNameTypeAndContentAndReturns201WithoutLocation()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var bytes = "圖片內容"u8.ToArray();

        // Act
        using var response = await client.PostAsync(PageRoute, AttachmentEndpointTests.Multipart("截圖 1.png", bytes, "image/png"));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var call = factory.Modules.KnowledgeAttachments.AddCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((call.Actor, call.SpaceKey, call.Number, call.FileName, call.ContentType), Is.EqualTo((TestUsers.Member.ToActor(), "HR", 2, "截圖 1.png", "image/png")));
            Assert.That(call.Content, Is.EqualTo(bytes));
            Assert.That(data?["fileName"]?.GetValue<string>(), Is.EqualTo("截圖 1.png"));
            Assert.That(data?["uploadedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("Member Example"));
            Assert.That(response.Headers.Location, Is.Null);
        }
    }

    [Test]
    public async Task Upload_NoFilePart_Returns400WithoutCallingTheService()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent { { new StringContent("只有欄位"), "note" } };

        // Act
        using var response = await client.PostAsync(PageRoute, content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.KnowledgeAttachments.AddCalls, Is.Empty);
    }

    [Test]
    public async Task Upload_AJsonBody_Returns415()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(PageRoute, ApiRequests.Json(new { file = "x" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType);
    }

    [Test]
    public async Task Upload_ADeclaredLengthOverTheRequestLimit_Returns413WithTheKnowledgeCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(new SizedContent(26L * 1024 * 1024 + 1, knownLength: true), "file", "huge.bin");

        // Act
        using var response = await client.PostAsync(PageRoute, content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, KnowledgeErrorCodes.AttachmentTooLarge);
        Assert.That(factory.Modules.KnowledgeAttachments.AddCalls, Is.Empty);
    }

    [Test]
    public async Task Upload_AChunkedBodyOverTheRequestLimit_Returns413WithTheKnowledgeCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(new SizedContent(30L * 1024 * 1024, knownLength: false), "file", "huge.bin");
        using var request = new HttpRequestMessage(HttpMethod.Post, PageRoute) { Content = content };
        request.Headers.TransferEncodingChunked = true;

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, KnowledgeErrorCodes.AttachmentTooLarge);
    }

    [Test]
    public void TheKnowledgeAttachmentLimit_IsTheSameAsTheItemAttachmentLimit()
    {
        // Arrange、Act、Assert：上傳的請求上限共用同一個常數，兩邊的檔案上限不一樣就會悄悄壞掉。
        Assert.That(KnowledgeRules.MaxAttachmentBytes, Is.EqualTo(AttachmentRules.MaxBytes));
    }

    [TestCase(FailureKind.Invalid, KnowledgeErrorCodes.AttachmentTooLarge, HttpStatusCode.RequestEntityTooLarge)]
    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.AttachmentLimitReached, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.AttachmentQuotaExceeded, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.AttachmentDailyQuotaExceeded, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.SpaceArchived, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Forbidden, KnowledgeErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, KnowledgeErrorCodes.PageNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Invalid, KnowledgeErrorCodes.AttachmentNameInvalid, HttpStatusCode.UnprocessableEntity)]
    public async Task Upload_ServiceFailure_IsMappedToTheStatusAndCode(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgeAttachments.OnAdd = _ => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(PageRoute, AttachmentEndpointTests.Multipart("a.txt", [1], "text/plain"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [TestCase("image/png")]
    [TestCase("image/jpeg")]
    [TestCase("image/gif")]
    [TestCase("image/webp")]
    public async Task Download_AllowedImageTypes_AreServedInline(string contentType)
    {
        // Arrange
        using var factory = FactoryServing(KnowledgeSamples.Attachment(fileName: "截圖.png", contentType: contentType));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(DownloadRoute());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo(contentType));
            Assert.That(Header(response, "Content-Disposition"), Does.StartWith("inline;"));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("hello world"));
        }
    }

    [TestCase("application/pdf")]
    [TestCase("text/html")]
    [TestCase("image/svg+xml")]
    [TestCase("text/plain")]
    public async Task Download_AnyOtherType_IsServedAsAnOctetStreamAttachmentWithTheSecurityHeaders(string contentType)
    {
        // Arrange
        using var factory = FactoryServing(KnowledgeSamples.Attachment(fileName: "x.bin", contentType: contentType));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(DownloadRoute());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(Header(response, "Content-Disposition"), Does.StartWith("attachment;"));
            Assert.That(Header(response, "X-Content-Type-Options"), Is.EqualTo("nosniff"));
            Assert.That(Header(response, "Content-Security-Policy"), Is.EqualTo("default-src 'none'; sandbox"));
            Assert.That(Header(response, "X-Attachment-Sha256"), Is.EqualTo(KnowledgeSamples.Attachment().Sha256));
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(11));
        }
    }

    [Test]
    public async Task Download_NotFoundForTheCaller_Returns404WithTheKnowledgeCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgeAttachments.OnOpen = _ => KnowledgeSamples.NotFound(KnowledgeErrorCodes.AttachmentNotFound);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(DownloadRoute());

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, KnowledgeErrorCodes.AttachmentNotFound);
    }

    [TestCase("not-a-guid")]
    [TestCase("12345")]
    [TestCase("0190a0000000700080000000000000a1")]
    public async Task Download_AnIdThatIsNotInTheIdFormat_IsNotFoundWithoutCallingTheService(string id)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"/v1/knowledge/attachments/{id}/content");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, KnowledgeErrorCodes.AttachmentNotFound);
        Assert.That(factory.Modules.KnowledgeAttachments.OpenCalls, Is.Empty);
    }

    [Test]
    public async Task EveryEndpoint_WithoutSigningIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var list = await client.GetAsync(PageRoute);
        using var upload = await client.PostAsync(PageRoute, AttachmentEndpointTests.Multipart("a.txt", [1], "text/plain"));
        using var download = await client.GetAsync(DownloadRoute());

        // Assert
        Assert.That(new[] { list, upload, download }.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Modules.KnowledgeAttachments.AddCalls, Is.Empty);
    }

    private static ApiFactory FactoryServing(PageAttachment attachment)
    {
        var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgeAttachments.OnOpen = _ => (attachment, "hello world"u8.ToArray());
        return factory;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single()
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? contentValues.Single()
        : null;

    /// <summary>全是 0 的內容，不用真的配置記憶體；可以選擇要不要告訴伺服器長度。</summary>
    private sealed class SizedContent(long length, bool knownLength) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[64 * 1024];
            for (var sent = 0L; sent < length; sent += buffer.Length)
            {
                await stream.WriteAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - sent)));
            }
        }

        protected override bool TryComputeLength(out long computed)
        {
            computed = knownLength ? length : 0;
            return knownLength;
        }
    }
}
