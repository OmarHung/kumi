using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// AI 助理附件的三個端點與送訊息的 attachmentIds，對假的服務：形狀、上傳的讀法、失敗對應的狀態碼、下載的標頭、限流。
/// 端到端（真的模組、真的資料庫）在 <c>RealStack/AssistantAttachmentFlowTests</c>。
/// </summary>
[TestFixture]
public sealed class AssistantAttachmentEndpointTests
{
    private const string Collection = "/v1/projects/WEB/assistant/attachments";
    private const string MessagesRoute = AssistantEndpointTests.Collection + "/22222222-2222-2222-2222-222222222222/messages";

    private static readonly string Item = $"{Collection}/{FakeAssistantAttachmentService.SampleId}";

    private static readonly string Content = Item + "/content";

    [Test]
    public async Task Upload_AMultipartFile_PassesProjectNameTypeAndContent_AndReturns201WithTheContractShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var bytes = "圖片內容"u8.ToArray();

        // Act
        using var response = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("截圖 1.png", bytes, "image/png"));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var call = factory.Modules.AssistantAttachments.UploadCalls.Single();
        var expected = JsonNode.Parse(
            $$"""
            {"id":"{{FakeAssistantAttachmentService.SampleId}}","fileName":"截圖 1.png","contentType":"image/png","sizeBytes":{{bytes.Length}},
             "kind":"image","uploadedAt":"2026-11-26T03:00:00Z"}
            """);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((call.Actor, call.ProjectKey, call.FileName, call.ContentType), Is.EqualTo((TestUsers.Member.ToActor(), "WEB", "截圖 1.png", "image/png")));
            Assert.That(call.Content, Is.EqualTo(bytes));
            Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
            Assert.That(response.Headers.Location, Is.Null);
        }
    }

    [TestCase(AssistantAttachmentKind.Image, "image")]
    [TestCase(AssistantAttachmentKind.Pdf, "pdf")]
    [TestCase(AssistantAttachmentKind.Text, "text")]
    public async Task Upload_Kind_IsWrittenAsLowerCaseSnakeCase(AssistantAttachmentKind kind, string expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AssistantAttachments.OnUpload = _ => FakeAssistantAttachmentService.Sample(kind: kind);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("a", [1], "text/plain"));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        Assert.That(data?["kind"]?.GetValue<string>(), Is.EqualTo(expected));
    }

    [Test]
    public async Task Upload_NoFilePart_Returns400WithoutCallingTheService()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent { { new StringContent("只有欄位"), "note" } };

        // Act
        using var response = await client.PostAsync(Collection, content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.AssistantAttachments.UploadCalls, Is.Empty);
    }

    [Test]
    public async Task Upload_AJsonBody_Returns415()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Collection, ApiRequests.Json(new { file = "x" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType);
    }

    [Test]
    public async Task Upload_ADeclaredLengthOverTheRequestLimit_Returns413WithTheAssistantCode()
    {
        // Arrange：上限是 PDF 的 10 MiB 加 multipart 的 1 MiB，不是項目附件的 25 MiB。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(new SizedContent(11L * 1024 * 1024 + 1, knownLength: true), "file", "huge.bin");

        // Act
        using var response = await client.PostAsync(Collection, content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, AiErrorCodes.AttachmentTooLarge);
        Assert.That(factory.Modules.AssistantAttachments.UploadCalls, Is.Empty);
    }

    [Test]
    public async Task Upload_AChunkedBodyOverTheRequestLimit_Returns413WithTheAssistantCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(new SizedContent(13L * 1024 * 1024, knownLength: false), "file", "huge.bin");
        using var request = new HttpRequestMessage(HttpMethod.Post, Collection) { Content = content };
        request.Headers.TransferEncodingChunked = true;

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, AiErrorCodes.AttachmentTooLarge);
    }

    [TestCase(FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge, HttpStatusCode.RequestEntityTooLarge)]
    [TestCase(FailureKind.Invalid, AiErrorCodes.AttachmentTypeNotAllowed, HttpStatusCode.UnprocessableEntity)]
    [TestCase(FailureKind.Invalid, AiErrorCodes.AttachmentNameInvalid, HttpStatusCode.UnprocessableEntity)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.TooManyAttachments, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.AttachmentQuotaExceeded, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, AiErrorCodes.AttachmentDailyQuotaExceeded, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Forbidden, AiErrorCodes.AssistantForbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, "project.not_found", HttpStatusCode.NotFound)]
    public async Task Upload_ServiceFailure_IsMappedToTheStatusAndCode(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AssistantAttachments.OnUpload = _ => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("a.txt", [1], "text/plain"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task Upload_TheConnectionDropsWhileTheServiceReadsTheBody_Returns400NotA500()
    {
        // Arrange：內容是一路串流進模組的，客戶端中途斷線時讀取的 IOException 是在模組裡丟出來的。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AssistantAttachments.OnUpload = _ => throw new IOException("客戶端斷線了。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("a.png", [1, 2, 3], "image/png"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
    }

    [Test]
    public async Task Upload_NameInvalid_KeepsTheDetailPath()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AssistantAttachments.OnUpload = _ => new Failure(
            FailureKind.Invalid, AiErrorCodes.AttachmentNameInvalid, "檔名不合規則。")
        {
            Details = [new FailureDetail(AiErrorCodes.AttachmentNameInvalid, "fileName", "檔名不能有路徑。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("a/b", [1], "text/plain"));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AttachmentNameInvalid);

        // Assert
        Assert.That(details.Single()?["path"]?.GetValue<string>(), Is.EqualTo("fileName"));
    }

    [Test]
    public async Task Upload_BeyondThirtyPerMinute_Returns429_AndDeletesAndDownloadsHaveTheirOwnBudget()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < ApiLimits.AssistantAttachmentUploadPermitsPerWindow; i++)
        {
            using var accepted = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("a.txt", [1], "text/plain"));
            statuses.Add(accepted.StatusCode);
        }

        // Act
        using var over = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("a.txt", [1], "text/plain"));
        using var delete = await client.DeleteAsync(Item);
        using var download = await client.GetAsync(Content);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiLimits.AssistantAttachmentUploadPermitsPerWindow, Is.EqualTo(30));
            Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.Created));
            Assert.That(over.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task Delete_ReturnsNoContent_AndPassesTheProjectAndId()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.DeleteAsync(Item);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(factory.Modules.AssistantAttachments.DeleteCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), "WEB", FakeAssistantAttachmentService.SampleId)));
        }
    }

    [Test]
    public async Task Delete_NotFound_Returns404WithTheAttachmentCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AssistantAttachments.OnDelete = (_, _, _) => new Failure(FailureKind.NotFound, AiErrorCodes.AttachmentNotFound, "找不到。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync(Item);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [TestCase("not-a-guid")]
    [TestCase("12345")]
    [TestCase("0190a0000000700080000000000000b1")]
    public async Task DeleteAndDownload_AnIdThatIsNotInTheIdFormat_AreNotFoundWithoutCallingTheService(string id)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var delete = await client.DeleteAsync($"{Collection}/{id}");
        using var download = await client.GetAsync($"{Collection}/{id}/content");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(delete, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(download, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.AssistantAttachments.DeleteCalls, Is.Empty);
            Assert.That(factory.Modules.AssistantAttachments.OpenCalls, Is.Empty);
        }
    }

    [TestCase("image/png")]
    [TestCase("image/jpeg")]
    [TestCase("image/gif")]
    [TestCase("image/webp")]
    public async Task Download_BitmapImages_AreServedInline(string contentType)
    {
        // Arrange
        using var factory = FactoryServing(FakeAssistantAttachmentService.Sample(contentType: contentType));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Content);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo(contentType));
            Assert.That(Header(response, "Content-Disposition"), Does.StartWith("inline;"));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("hello world"));
            Assert.That(factory.Modules.AssistantAttachments.OpenCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), "WEB", FakeAssistantAttachmentService.SampleId)));
        }
    }

    [TestCase("application/pdf", AssistantAttachmentKind.Pdf)]
    [TestCase("text/plain", AssistantAttachmentKind.Text)]
    [TestCase("image/svg+xml", AssistantAttachmentKind.Text)]
    [TestCase("text/html", AssistantAttachmentKind.Text)]
    public async Task Download_AnyOtherType_IsServedAsAnOctetStreamAttachmentWithTheSecurityHeaders(string contentType, AssistantAttachmentKind kind)
    {
        // Arrange
        using var factory = FactoryServing(FakeAssistantAttachmentService.Sample(fileName: "x.bin", contentType: contentType, kind: kind));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Content);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(Header(response, "Content-Disposition"), Does.StartWith("attachment;"));
            Assert.That(Header(response, "X-Content-Type-Options"), Is.EqualTo("nosniff"));
            Assert.That(Header(response, "Content-Security-Policy"), Is.EqualTo("default-src 'none'; sandbox"));
            Assert.That(Header(response, "X-Attachment-Sha256"), Is.EqualTo("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9"));
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(11));
        }
    }

    [Test]
    public async Task Download_NotFoundForTheCaller_Returns404WithTheAttachmentCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AssistantAttachments.OnOpen = (_, _, _) => new Failure(FailureKind.NotFound, AiErrorCodes.AttachmentNotFound, "找不到。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task EveryEndpoint_WithoutSigningIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var upload = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("a.txt", [1], "text/plain"));
        using var delete = await client.DeleteAsync(Item);
        using var download = await client.GetAsync(Content);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new[] { upload, delete, download }.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(factory.Modules.AssistantAttachments.UploadCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Send_AttachmentIds_ArePassedToTheModuleInOrder_AndOmittedMeansNone()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var first = Guid.Parse("0190a000-0000-7000-8000-0000000000b2");
        var second = Guid.Parse("0190a000-0000-7000-8000-0000000000b1");
        var selection = new { connectionId = FakeAssistantService.ConnectionId, model = "claude-opus-5-5" };

        // Act
        using var with = await SendAsync(client, new { text = "", selection, attachmentIds = new[] { first, second } });
        await with.Content.ReadAsStringAsync();
        using var without = await SendAsync(client, new { text = "你好", selection });
        await without.Content.ReadAsStringAsync();
        using var explicitNull = await SendAsync(client, new { text = "你好", selection, attachmentIds = (Guid[]?)null });
        await explicitNull.Content.ReadAsStringAsync();

        // Assert
        var calls = factory.Modules.Assistant.StartCalls;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls[0].Input.AttachmentIds, Is.EqualTo(new[] { first, second }));
            Assert.That(calls[0].Input.Text, Is.Empty);
            Assert.That(calls[1].Input.AttachmentIds, Is.Empty);
            Assert.That(calls[2].Input.AttachmentIds, Is.Empty);
        }
    }

    [TestCase(AiErrorCodes.AttachmentNotFound)]
    [TestCase(AiErrorCodes.TooManyAttachments)]
    [TestCase(AiErrorCodes.AttachmentTooLarge)]
    [TestCase(AiErrorCodes.AttachmentUnsupported)]
    public async Task Send_AttachmentFailures_Are422WithTheirCodes(string code)
    {
        // Arrange：送訊息時合計太大、數量太多都是 422（上傳單一檔案太大才是 413）。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Assistant.OnStart = _ => new Failure(FailureKind.Invalid, code, "附件有問題。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await SendAsync(client, new
        {
            text = "看這個",
            selection = new { connectionId = FakeAssistantService.ConnectionId, model = "m" },
            attachmentIds = new[] { FakeAssistantAttachmentService.SampleId },
        });

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, code);
    }

    [Test]
    public async Task Get_UserMessageWithAttachments_WritesThemInOrder_AndTheReplyWritesAnEmptyArray()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var pdf = FakeAssistantAttachmentService.Sample(
            Guid.Parse("0190a000-0000-7000-8000-0000000000b2"), "規格.pdf", "application/pdf", 2048, AssistantAttachmentKind.Pdf);
        factory.Modules.Assistant.OnGet = (_, _, _) => new AssistantConversation(
            FakeAssistantService.Summary(messageCount: 2),
            [FakeAssistantService.UserMessage() with { Attachments = [FakeAssistantAttachmentService.Sample(), pdf] }, FakeAssistantService.AssistantReply()]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(AssistantEndpointTests.Item);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var attachments = data?["messages"]?[0]?["attachments"];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attachments?.AsArray().Select(a => a?["kind"]?.GetValue<string>()), Is.EqualTo(new[] { "image", "pdf" }));
            Assert.That(attachments?[1]?["fileName"]?.GetValue<string>(), Is.EqualTo("規格.pdf"));
            Assert.That(attachments?[1]?["sizeBytes"]?.GetValue<long>(), Is.EqualTo(2048));
            Assert.That(data?["messages"]?[1]?["attachments"]?.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task Send_StartEvent_CarriesTheUserMessagesAttachments()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var run = FakeAssistantService.QuickRun().Value;
        factory.Modules.Assistant.OnStart = _ => run with
        {
            UserMessage = run.UserMessage with { Attachments = [FakeAssistantAttachmentService.Sample()] },
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await SendAsync(client, new
        {
            text = "看這個",
            selection = new { connectionId = FakeAssistantService.ConnectionId, model = "m" },
            attachmentIds = new[] { FakeAssistantAttachmentService.SampleId },
        });
        var frames = await new AssistantStreamTests.SseReader(response).ReadToEndAsync();

        // Assert
        var userMessage = JsonNode.Parse(frames[0].Data!)?["userMessage"];
        var done = JsonNode.Parse(frames[^1].Data!)?["message"];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(userMessage?["attachments"]?[0]?["id"]?.GetValue<Guid>(), Is.EqualTo(FakeAssistantAttachmentService.SampleId));
            Assert.That(done?["attachments"]?.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task Get_UserMessageWithOnlyAttachments_WritesEmptyParts()
    {
        // Arrange：只附檔案沒打字，模組存的使用者訊息沒有文字段落。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Assistant.OnGet = (_, _, _) => new AssistantConversation(
            FakeAssistantService.Summary(messageCount: 1),
            [FakeAssistantService.UserMessage() with { Parts = [], Attachments = [FakeAssistantAttachmentService.Sample()] }]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(AssistantEndpointTests.Item);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["messages"]?[0]?["parts"]?.AsArray(), Is.Empty);
            Assert.That(data?["messages"]?[0]?["attachments"]?.AsArray(), Has.Count.EqualTo(1));
        }
    }

    private static ApiFactory FactoryServing(AssistantAttachment attachment)
    {
        var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AssistantAttachments.OnOpen = (_, _, _) => (attachment, "hello world"u8.ToArray());
        return factory;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single()
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? contentValues.Single()
        : null;

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MessagesRoute) { Content = ApiRequests.Json(body) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

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
