using System.Net;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// 不選專案的組織對話的附件端點（<c>/v1/assistant/attachments…</c>）：和專案的附件同一套處理，交給模組的專案代號是 null，
/// 上傳和專案的附件共用同一個依人的額度，只能用 Cookie。規則（權限、兩邊分開）在 AI 模組的測試。
/// </summary>
[TestFixture]
public sealed class AssistantOrganizationAttachmentEndpointTests
{
    private const string Collection = "/v1/assistant/attachments";

    private const string ProjectCollection = "/v1/projects/WEB/assistant/attachments";

    private static readonly string Item = $"{Collection}/{FakeAssistantAttachmentService.SampleId}";

    private static readonly string Content = Item + "/content";

    [Test]
    public async Task Upload_PassesANullProjectAndReturns201()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var bytes = "# 需求"u8.ToArray();

        // Act
        using var response = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("需求.md", bytes, "text/markdown"));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var call = factory.Modules.AssistantAttachments.UploadCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((call.Actor, call.ProjectKey, call.FileName), Is.EqualTo((TestUsers.Member.ToActor(), (string?)null, "需求.md")));
            Assert.That(call.Content, Is.EqualTo(bytes));
            Assert.That(data?["id"]?.GetValue<Guid>(), Is.EqualTo(FakeAssistantAttachmentService.SampleId));
        }
    }

    [Test]
    public async Task Upload_TooLargeFromTheModule_Is413LikeTheProjectEndpoint()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.AssistantAttachments.OnUpload = _ => new Failure(FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge, "太大。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("a.png", [1], "image/png"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, AiErrorCodes.AttachmentTooLarge);
    }

    [Test]
    public async Task DeleteAndDownload_PassANullProject()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var delete = await client.DeleteAsync(Item);
        using var download = await client.GetAsync(Content);
        var bytes = await download.Content.ReadAsByteArrayAsync();

        // Assert
        var expected = (TestUsers.Member.ToActor(), (string?)null, FakeAssistantAttachmentService.SampleId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(bytes, Is.EqualTo("hello world"u8.ToArray()));
            Assert.That(factory.Modules.AssistantAttachments.DeleteCalls.Single(), Is.EqualTo(expected));
            Assert.That(factory.Modules.AssistantAttachments.OpenCalls.Single(), Is.EqualTo(expected));
        }
    }

    [Test]
    public async Task DeleteAndDownload_NotFound_Return404WithTheAttachmentCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var notFound = new Failure(FailureKind.NotFound, AiErrorCodes.AttachmentNotFound, "找不到。");
        factory.Modules.AssistantAttachments.OnDelete = (_, _, _) => notFound;
        factory.Modules.AssistantAttachments.OnOpen = (_, _, _) => notFound;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var delete = await client.DeleteAsync(Item);
        using var download = await client.GetAsync(Content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(delete, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(download, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task DeleteAndDownload_AnIdThatIsNotInTheIdFormat_AreNotFoundWithoutCallingTheService()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var delete = await client.DeleteAsync($"{Collection}/not-a-guid");
        using var download = await client.GetAsync($"{Collection}/not-a-guid/content");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(delete, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
            await ApiRequests.AssertErrorEnvelopeAsync(download, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
            Assert.That(factory.Modules.AssistantAttachments.DeleteCalls, Is.Empty);
            Assert.That(factory.Modules.AssistantAttachments.OpenCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Upload_SharesThePerPersonBudgetWithTheProjectEndpoint()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        for (var i = 0; i < ApiLimits.AssistantAttachmentUploadPermitsPerWindow; i++)
        {
            using var accepted = await client.PostAsync(ProjectCollection, AttachmentEndpointTests.Multipart("a.txt", [1], "text/plain"));
            Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        }

        // Act
        using var over = await client.PostAsync(Collection, AttachmentEndpointTests.Multipart("a.txt", [1], "text/plain"));

        // Assert
        Assert.That(over.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
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
}
