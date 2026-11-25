using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>附件的四個端點，對假的附件服務：形狀、上傳的讀法、失敗的對應。下載的標頭在 <see cref="AttachmentDownloadTests"/>。</summary>
[TestFixture]
public sealed class AttachmentEndpointTests
{
    private const string ItemRoute = "/v1/items/WEB-7/attachments";

    [Test]
    public async Task ListAttachments_ReturnsTheContractShapeWithTheUploaderName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(ItemRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            $$"""
            [{"id":"{{FakeAttachmentService.SampleId}}","itemKey":"WEB-7","fileName":"規格.pdf","contentType":"application/pdf","sizeBytes":11,
              "sha256":"b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
              "uploadedBy":{"personId":"{{TestUsers.Member.PersonId}}","displayName":"{{TestUsers.Member.DisplayName}}"},
              "uploadedAt":"2026-11-19T03:00:00Z"}]
            """);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.Attachments.ListCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB-7") }));
        }
    }

    [Test]
    public async Task ListAttachments_Empty_ReturnsAnEmptyArray()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Attachments.OnList = (_, _) => new List<Attachment>();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(ItemRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?.ToJsonString(), Is.EqualTo("[]"));
    }

    [Test]
    public async Task Upload_AMultipartFile_PassesTheNameTypeAndContentToTheServiceAndReturns201()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var bytes = "報價單內容"u8.ToArray();

        // Act
        using var response = await client.PostAsync(ItemRoute, Multipart("規格 (最終).pdf", bytes, "application/pdf"));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var call = factory.Modules.Attachments.AddCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((call.Actor, call.ItemKey, call.FileName, call.ContentType), Is.EqualTo((TestUsers.Member.ToActor(), "WEB-7", "規格 (最終).pdf", "application/pdf")));
            Assert.That(call.Content, Is.EqualTo(bytes));
            Assert.That(data?["fileName"]?.GetValue<string>(), Is.EqualTo("規格 (最終).pdf"));
            Assert.That(data?["sizeBytes"]?.GetValue<long>(), Is.EqualTo(bytes.Length));
            Assert.That(data?["uploadedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.DisplayName));
            Assert.That(response.Headers.Location, Is.Null);
        }
    }

    [Test]
    public async Task Upload_OtherFieldsBeforeTheFile_AreSkipped()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent { { new StringContent("備註"), "note" } };
        content.Add(FilePart("a.txt", "x"u8.ToArray(), "text/plain"), "file", "a.txt");

        // Act
        using var response = await client.PostAsync(ItemRoute, content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(factory.Modules.Attachments.AddCalls.Single().FileName, Is.EqualTo("a.txt"));
    }

    [Test]
    public async Task Upload_AFileWithoutAContentType_PassesNull()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent([1, 2, 3]);
        content.Add(part, "file", "raw.bin");

        // Act
        using var response = await client.PostAsync(ItemRoute, content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(factory.Modules.Attachments.AddCalls.Single().ContentType, Is.Null);
    }

    [Test]
    public async Task Upload_NoFilePart_Returns400WithoutCallingTheService()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent { { new StringContent("只有欄位"), "note" } };

        // Act
        using var response = await client.PostAsync(ItemRoute, content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.Attachments.AddCalls, Is.Empty);
    }

    [Test]
    public async Task Upload_AFilePartWithTheWrongFieldName_Returns400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(FilePart("a.txt", [1], "text/plain"), "upload", "a.txt");

        // Act
        using var response = await client.PostAsync(ItemRoute, content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
    }

    [Test]
    public async Task Upload_AJsonBody_Returns415()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(ItemRoute, ApiRequests.Json(new { file = "x" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType);
        Assert.That(factory.Modules.Attachments.AddCalls, Is.Empty);
    }

    [Test]
    public async Task Upload_AMultipartWithoutABoundary_Returns415()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = ApiRequests.Bytes([1, 2, 3], "multipart/form-data");

        // Act
        using var response = await client.PostAsync(ItemRoute, content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType);
    }

    [Test]
    public async Task Upload_ADeclaredLengthOverTheRequestLimit_Returns413WithoutReadingOrCallingTheService()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(new SizedContent(26L * 1024 * 1024 + 1, knownLength: true), "file", "huge.bin");

        // Act
        using var response = await client.PostAsync(ItemRoute, content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, WorkErrorCodes.AttachmentTooLarge);
        Assert.That(factory.Modules.Attachments.AddCalls, Is.Empty);
    }

    [Test]
    public async Task Upload_AChunkedBodyOverTheRequestLimit_Returns413WhenTheLimitIsCrossed()
    {
        // Arrange：沒有 Content-Length，只能讀到超過才知道。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(new SizedContent(30L * 1024 * 1024, knownLength: false), "file", "huge.bin");
        using var request = new HttpRequestMessage(HttpMethod.Post, ItemRoute) { Content = content };
        request.Headers.TransferEncodingChunked = true;

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, WorkErrorCodes.AttachmentTooLarge);
    }

    [Test]
    public async Task Upload_AFileJustUnderTheRequestLimit_IsPassedToTheServiceInFull()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(new SizedContent(AttachmentRules.MaxBytes, knownLength: true), "file", "max.bin");

        // Act
        using var response = await client.PostAsync(ItemRoute, content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(factory.Modules.Attachments.AddCalls.Single().Content, Has.Length.EqualTo(AttachmentRules.MaxBytes));
    }

    [TestCaseSource(nameof(UploadFailures))]
    public async Task Upload_ServiceFailure_IsMappedToTheStatusAndCode(Failure failure, HttpStatusCode status, string code)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Attachments.OnAdd = _ => failure;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(ItemRoute, Multipart("a.txt", [1], "text/plain"));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, status, code);
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.EqualTo(failure.Details.Select(d => d.Path)));
    }

    public static IEnumerable<TestCaseData> UploadFailures()
    {
        yield return Failing(FailureKind.Invalid, WorkErrorCodes.AttachmentTooLarge, HttpStatusCode.RequestEntityTooLarge);
        yield return Failing(FailureKind.Conflict, WorkErrorCodes.AttachmentLimitReached, HttpStatusCode.Conflict);
        yield return Failing(FailureKind.Conflict, WorkErrorCodes.AttachmentQuotaExceeded, HttpStatusCode.Conflict);
        yield return Failing(FailureKind.Conflict, WorkErrorCodes.AttachmentDailyQuotaExceeded, HttpStatusCode.Conflict);
        yield return Failing(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, HttpStatusCode.Conflict);
        yield return Failing(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden);
        yield return Failing(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, HttpStatusCode.NotFound);
        yield return new TestCaseData(
                new Failure(FailureKind.Invalid, WorkErrorCodes.AttachmentNameInvalid, "檔名不合格")
                {
                    Details = [new FailureDetail(WorkErrorCodes.AttachmentNameInvalid, "fileName", "檔名不合格")],
                },
                HttpStatusCode.UnprocessableEntity,
                WorkErrorCodes.AttachmentNameInvalid)
            .SetArgDisplayNames(WorkErrorCodes.AttachmentNameInvalid);
    }

    [Test]
    public async Task Delete_ReturnsTheDeletedAttachment()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync($"/v1/attachments/{FakeAttachmentService.SampleId}");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["id"]?.GetValue<Guid>(), Is.EqualTo(FakeAttachmentService.SampleId));
            Assert.That(data?["uploadedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(factory.Modules.Attachments.DeleteCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), FakeAttachmentService.SampleId) }));
        }
    }

    [TestCase(FailureKind.NotFound, WorkErrorCodes.AttachmentNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, HttpStatusCode.Conflict)]
    public async Task Delete_ServiceFailure_IsMappedToTheStatusAndCode(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Attachments.OnDelete = (_, _) => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync($"/v1/attachments/{FakeAttachmentService.SampleId}");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [TestCase("not-a-guid")]
    [TestCase("12345")]
    [TestCase("0190a0000000700080000000000000a1")]
    public async Task DeleteAndDownload_AnIdThatIsNotInTheIdFormat_AreNotFoundWithoutCallingTheService(string id)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var deleted = await client.DeleteAsync($"/v1/attachments/{id}");
        using var downloaded = await client.GetAsync($"/v1/attachments/{id}/content");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(deleted, HttpStatusCode.NotFound, WorkErrorCodes.AttachmentNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(downloaded, HttpStatusCode.NotFound, WorkErrorCodes.AttachmentNotFound);
        Assert.That(factory.Modules.Attachments.DeleteCalls.Concat(factory.Modules.Attachments.OpenCalls.Select(c => c)), Is.Empty);
    }

    [Test]
    public async Task EveryAttachmentEndpoint_WithoutSigningIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var list = await client.GetAsync(ItemRoute);
        using var upload = await client.PostAsync(ItemRoute, Multipart("a.txt", [1], "text/plain"));
        using var download = await client.GetAsync($"/v1/attachments/{FakeAttachmentService.SampleId}/content");
        using var delete = await client.DeleteAsync($"/v1/attachments/{FakeAttachmentService.SampleId}");

        // Assert
        Assert.That(
            new[] { list, upload, download, delete }.Select(r => r.StatusCode),
            Is.All.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Modules.Attachments.AddCalls, Is.Empty);
    }

    internal static MultipartFormDataContent Multipart(string fileName, byte[] bytes, string contentType)
    {
        var content = new MultipartFormDataContent();
        content.Add(FilePart(fileName, bytes, contentType), "file", fileName);
        return content;
    }

    private static ByteArrayContent FilePart(string fileName, byte[] bytes, string contentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return part;
    }

    private static TestCaseData Failing(FailureKind kind, string code, HttpStatusCode status) =>
        new TestCaseData(new Failure(kind, code, "失敗"), status, code).SetArgDisplayNames(code);

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
