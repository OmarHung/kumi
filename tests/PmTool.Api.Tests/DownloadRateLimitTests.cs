using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>下載端點的次數上限：kumi 的執行檔一分鐘 10 次；附件（項目與知識庫共用一份）一分鐘 300 次，說明裡內嵌的圖片一頁就是幾十個請求。</summary>
[TestFixture]
public sealed class DownloadRateLimitTests
{
    private const int CliDownloadsPerWindow = 10;
    private const int AttachmentDownloadsPerWindow = 300;

    [Test]
    public async Task CliDownload_BeyondTenPerMinute_Returns429WithRetryAfter()
    {
        // Arrange：下載與清單各算各的額度。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < CliDownloadsPerWindow; i++)
        {
            using var accepted = await client.GetAsync("/v1/cli/download/macos-arm64");
            statuses.Add(accepted.StatusCode);
        }

        // Act
        using var response = await client.GetAsync("/v1/cli/download/macos-arm64");
        using var list = await client.GetAsync("/v1/cli");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses, Is.All.Not.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(response.Headers.RetryAfter?.Delta, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(list.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
        }
    }

    [Test]
    public async Task AttachmentDownload_ItemsAndKnowledgeShareOneBudgetOf300PerMinute()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var itemRoute = $"/v1/attachments/{FakeAttachmentService.SampleId}/content";
        var knowledgeRoute = $"/v1/knowledge/attachments/{KnowledgeSamples.AttachmentId}/content";
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < AttachmentDownloadsPerWindow; i++)
        {
            using var accepted = await client.GetAsync(i % 2 == 0 ? itemRoute : knowledgeRoute);
            statuses.Add(accepted.StatusCode);
        }

        // Act
        using var overItems = await client.GetAsync(itemRoute);
        using var overKnowledge = await client.GetAsync(knowledgeRoute);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses, Is.All.Not.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(overItems.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(overKnowledge.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        }
    }

    [Test]
    public async Task AttachmentDownload_OnePersonUsingUpTheBudget_DoesNotAffectAnother()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var heavy = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var other = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var route = $"/v1/attachments/{FakeAttachmentService.SampleId}/content";
        for (var i = 0; i < AttachmentDownloadsPerWindow; i++)
        {
            using var _ = await heavy.GetAsync(route);
        }

        // Act
        using var blocked = await heavy.GetAsync(route);
        using var free = await other.GetAsync(route);

        // Assert
        Assert.That(blocked.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(free.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
    }
}
