using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>GET /v1/attachments/{id}/content：使用者上傳的內容和網頁在同一個網域，標頭決定它會不會在這個網域執行。</summary>
[TestFixture]
public sealed class AttachmentDownloadTests
{
    private static string Route => $"/v1/attachments/{FakeAttachmentService.SampleId}/content";

    [TestCase("image/png")]
    [TestCase("image/jpeg")]
    [TestCase("image/gif")]
    [TestCase("image/webp")]
    public async Task Download_AllowedImageTypes_AreServedInlineWithTheirOwnType(string contentType)
    {
        // Arrange
        using var factory = FactoryServing(FakeAttachmentService.Sample(fileName: "截圖.png", contentType: contentType, size: 11));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

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
    [TestCase("text/javascript")]
    [TestCase("application/xhtml+xml")]
    [TestCase("text/xml")]
    [TestCase("image/bmp")]
    [TestCase("application/octet-stream")]
    [TestCase("text/plain")]
    public async Task Download_AnyOtherType_IsServedAsAnOctetStreamAttachment(string contentType)
    {
        // Arrange
        using var factory = FactoryServing(FakeAttachmentService.Sample(fileName: "x.bin", contentType: contentType));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(Header(response, "Content-Disposition"), Does.StartWith("attachment;"));
        }
    }

    [Test]
    public async Task Download_AlwaysCarriesTheSecurityHeadersAndTheLength()
    {
        // Arrange
        using var factory = FactoryServing(FakeAttachmentService.Sample(contentType: "text/html", size: 11));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Header(response, "X-Content-Type-Options"), Is.EqualTo("nosniff"));
            Assert.That(Header(response, "Content-Security-Policy"), Is.EqualTo("default-src 'none'; sandbox"));
            Assert.That(Header(response, "Cache-Control"), Is.EqualTo("private, max-age=0"));
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(11));
            Assert.That(Header(response, "X-Frame-Options"), Is.EqualTo("DENY"), "整站的其他標頭照舊");
        }
    }

    [Test]
    public async Task Download_CarriesTheSha256SoAClientCanVerifyWithoutAnotherRequest()
    {
        // Arrange：CLI 只拿到附件的編號時，也要能核對下載的內容（2026-11-19 CLI 工作包回報的缺口）。
        using var factory = FactoryServing(FakeAttachmentService.Sample(size: 11));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(Header(response, "X-Attachment-Sha256"), Is.EqualTo("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9"));
    }

    [Test]
    public async Task Download_AChineseFileName_HasAnEncodedFilenameStarAndAnAsciiFallback()
    {
        // Arrange
        using var factory = FactoryServing(FakeAttachmentService.Sample(fileName: "規格 (最終版).pdf", contentType: "application/pdf"));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(
            Header(response, "Content-Disposition"),
            Is.EqualTo("attachment; filename=\"__ (___).pdf\"; filename*=UTF-8''%E8%A6%8F%E6%A0%BC%20%28%E6%9C%80%E7%B5%82%E7%89%88%29.pdf"));
    }

    [Test]
    public async Task Download_AnAsciiFileName_StillCarriesBothForms()
    {
        // Arrange
        using var factory = FactoryServing(FakeAttachmentService.Sample(fileName: "report-v2.png", contentType: "image/png"));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(Header(response, "Content-Disposition"), Is.EqualTo("inline; filename=\"report-v2.png\"; filename*=UTF-8''report-v2.png"));
    }

    [Test]
    public async Task Download_AFileNameWithQuotesSemicolonsAndPercent_CannotBreakOutOfTheHeader()
    {
        // Arrange
        using var factory = FactoryServing(FakeAttachmentService.Sample(fileName: "a\";b%c'd*e,f\\g.txt", contentType: "text/plain"));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(
            Header(response, "Content-Disposition"),
            Is.EqualTo("attachment; filename=\"a__b_c'd*e_f_g.txt\"; filename*=UTF-8''a%22%3Bb%25c%27d%2Ae%2Cf%5Cg.txt"));
    }

    [Test]
    public async Task Download_AFileNameWithEmoji_IsPercentEncodedAsUtf8()
    {
        // Arrange
        using var factory = FactoryServing(FakeAttachmentService.Sample(fileName: "好\U0001F600.txt", contentType: "text/plain"));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(
            Header(response, "Content-Disposition"),
            Is.EqualTo("attachment; filename=\"__.txt\"; filename*=UTF-8''%E5%A5%BD%F0%9F%98%80.txt"));
    }

    [Test]
    public async Task Download_PassesTheActorAndTheIdToTheService()
    {
        // Arrange
        using var factory = FactoryServing(FakeAttachmentService.Sample());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(factory.Modules.Attachments.OpenCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), FakeAttachmentService.SampleId) }));
    }

    [Test]
    public async Task Download_NotFound_ReturnsTheEnvelopeWithoutDownloadHeaders()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Attachments.OnOpen = (_, _) => new PmTool.Abstractions.Failure(
            PmTool.Abstractions.FailureKind.NotFound, WorkErrorCodes.AttachmentNotFound, "找不到這個附件。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.AttachmentNotFound);
        Assert.That(response.Content.Headers.ContentDisposition, Is.Null);
    }

    [Test]
    public async Task OtherEndpoints_KeepTheSiteWideContentSecurityPolicy()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/items/WEB-7/attachments");

        // Assert
        Assert.That(Header(response, "Content-Security-Policy"), Does.StartWith("default-src 'self'"));
    }

    private static ApiFactory FactoryServing(Attachment attachment)
    {
        var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Attachments.OnOpen = (_, _) => (attachment, "hello world"u8.ToArray());
        return factory;
    }

    private static string Header(HttpResponseMessage response, string name) =>
        // 不經過 HttpClient 的解析：它會把 Cache-Control 的內容重排，測試要看伺服器實際送的字。
        response.Headers.NonValidated.TryGetValues(name, out var values) || response.Content.Headers.NonValidated.TryGetValues(name, out values)
            ? string.Join(",", values)
            : string.Empty;
}
