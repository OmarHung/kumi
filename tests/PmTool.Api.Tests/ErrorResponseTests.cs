using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class ErrorResponseTests
{
    private const string SecretDetail = "secret-detail-0f3a";

    [Test]
    public async Task UnknownRoute_Returns404Envelope()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/v1/no-such-route");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task ValidateRoute_WrongMethod_Returns405Envelope()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(ApiRequests.BlueprintRoute);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.MethodNotAllowed, ApiErrorCodes.MethodNotAllowed);
    }

    [TestCase("Development")]
    [TestCase("Production")]
    public async Task Validate_ValidatorThrows_Returns500WithoutExceptionDetails(string environment)
    {
        // Arrange
        var validator = FakeBlueprintValidator.Throwing(new InvalidOperationException(SecretDetail));
        using var factory = new ApiFactory(validator, new ApiFactoryOptions { Environment = environment });
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.InternalServerError, ApiErrorCodes.InternalError);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Does.Not.Contain(SecretDetail));
            Assert.That(body, Does.Not.Contain(nameof(InvalidOperationException)));
            Assert.That(body, Does.Not.Contain("at PmTool"));
        }
    }

    [Test]
    public async Task Validate_ValidatorThrowsNotImplemented_Returns500Envelope()
    {
        // Arrange
        var validator = FakeBlueprintValidator.Throwing(new NotImplementedException(SecretDetail));
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.ChangeSetRoute, ApiRequests.Document("a: 1", "text/yaml"));
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.InternalServerError, ApiErrorCodes.InternalError);
        Assert.That(body, Does.Not.Contain(SecretDetail));
    }
}
