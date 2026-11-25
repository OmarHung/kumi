using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Identity;
using PmTool.Storage;

namespace PmTool.Api.Tests;

/// <summary>GET /v1/storage-usage：只給 Cookie、只有組織管理員、回應的形狀與限制為 0 時的 null。</summary>
[TestFixture]
public sealed class StorageEndpointTests
{
    private const string Route = "/v1/storage-usage";

    [Test]
    public async Task Read_Owner_ReturnsTheLimitsTheTotalAndEachArea()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.StorageQuota.Report = new StorageUsageReport(
            20L * StorageQuotaSettings.GiB,
            2L * StorageQuotaSettings.GiB,
            [new StorageAreaUsage(StorageArea.ItemAttachments, 1_073_741_824, 312), new StorageAreaUsage(StorageArea.KnowledgeAttachments, 214_748_364, 40)]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            """
            {"organizationLimitBytes":21474836480,"personDailyLimitBytes":2147483648,"totalBytes":1288490188,
             "areas":[{"area":"item_attachments","bytes":1073741824,"files":312},
                      {"area":"knowledge_attachments","bytes":214748364,"files":40}]}
            """);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        }
    }

    [Test]
    public async Task Read_WhenNoLimitIsConfigured_ReturnsNullLimits()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.StorageQuota.Report = new StorageUsageReport(
            null, null, [new StorageAreaUsage(StorageArea.ItemAttachments, 0, 0), new StorageAreaUsage(StorageArea.KnowledgeAttachments, 0, 0)]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["organizationLimitBytes"], Is.Null);
            Assert.That(data?["personDailyLimitBytes"], Is.Null);
            Assert.That(data?["totalBytes"]?.GetValue<long>(), Is.Zero);
        }
    }

    [Test]
    public async Task Read_AMember_Returns403StorageForbidden()
    {
        // Arrange：預設的假配額對非管理員回 Forbidden。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, StorageErrorCodes.Forbidden);
    }

    [Test]
    public async Task Read_NotSignedIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task Read_AccessTokenWithEveryScope_Returns403InsufficientScope()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead,
            AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, Route, secret));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }
}
