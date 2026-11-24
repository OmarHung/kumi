using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class HostingTests
{
    [Test]
    public async Task OpenApi_Development_ServesDocumentWithBothValidateRoutes()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Environment = "Development" });
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain(ApiRequests.BlueprintRoute));
            Assert.That(body, Does.Contain(ApiRequests.ChangeSetRoute));
        }
    }

    [Test]
    public async Task OpenApi_Development_DescribesTheActivityAndCommentRoutes()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Environment = "Development" });
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/openapi/v1.json");
        var paths = (await ApiRequests.ReadEnvelopeAsync(response))["paths"]?.AsObject();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(paths?["/v1/items/{itemKey}/activity"]?["get"], Is.Not.Null, paths?.ToJsonString());
            Assert.That(paths?["/v1/items/{itemKey}/comments"]?["post"], Is.Not.Null, paths?.ToJsonString());
        }
    }

    [Test]
    public async Task OpenApi_Development_DescribesTheChangeSetRoutes()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Environment = "Development" });
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/openapi/v1.json");
        var paths = (await ApiRequests.ReadEnvelopeAsync(response))["paths"]?.AsObject();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(paths?["/v1/projects/{projectKey}/changesets"]?["get"], Is.Not.Null, paths?.ToJsonString());
            Assert.That(paths?["/v1/projects/{projectKey}/changesets"]?["post"], Is.Not.Null, paths?.ToJsonString());
            Assert.That(paths?["/v1/projects/{projectKey}/changesets/{number}"]?["get"], Is.Not.Null, paths?.ToJsonString());
            Assert.That(paths?["/v1/projects/{projectKey}/changesets/{number}/apply"]?["post"], Is.Not.Null, paths?.ToJsonString());
            Assert.That(paths?["/v1/projects/{projectKey}/changesets/{number}/reject"]?["post"], Is.Not.Null, paths?.ToJsonString());
        }
    }

    [Test]
    public async Task OpenApi_Production_Returns404Envelope()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Environment = "Production" });
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/openapi/v1.json");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
    }

    [Test]
    public void Host_ValidatorCannotBeCreated_FailsAtStartupInsteadOfOnTheFirstRequest()
    {
        // Arrange：模擬內嵌的 Schema 有問題。這時候健康檢查不該是好的。
        var options = new ApiFactoryOptions { ValidatorConstructionFails = true };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains(ApiFactory.ConstructionFailureDetail));
    }

    [Test]
    public void BlueprintPlanner_IsRegisteredForTheModulesToUse()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act
        var planner = factory.Services.GetService<IBlueprintPlanner>();

        // Assert
        Assert.That(planner, Is.InstanceOf<BlueprintPlanner>());
    }

    [Test]
    public void BlueprintValidator_IsRegisteredAsSingletonThroughFactoryDelegate()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act
        _ = factory.Services.GetRequiredService<IBlueprintValidator>();
        var registration = factory.AppValidatorRegistrations.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registration.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
            Assert.That(registration.ImplementationFactory, Is.Not.Null);
            Assert.That(registration.ImplementationInstance, Is.Null);
            Assert.That(registration.ImplementationType, Is.Null);
        }
    }
}
