using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class ValidationEndpointTests
{
    private static readonly string[] Routes = [ApiRequests.BlueprintRoute, ApiRequests.ChangeSetRoute];

    [TestCaseSource(nameof(Routes))]
    public async Task Validate_ValidDocument_ReturnsSuccessEnvelopeWithValidTrue(string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(route, ApiRequests.Document("{}"));
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(envelope["success"]?.GetValue<bool>(), Is.True);
            Assert.That(envelope["data"]?["valid"]?.GetValue<bool>(), Is.True);
            Assert.That(envelope["data"]?["issues"]?.AsArray(), Is.Empty);
            Assert.That(envelope["data"]?["truncated"]?.GetValue<bool>(), Is.False);
            Assert.That(envelope.ContainsKey("error") && envelope["error"] is null, Is.True, "error 要寫出 null");
        }
    }

    [TestCaseSource(nameof(Routes))]
    public async Task Validate_DocumentWithIssues_ReturnsSuccessEnvelopeWithIssues(string route)
    {
        // Arrange
        var report = new ValidationReport(
            [
                new ValidationIssue("structure.enum", "/workflows/0/states/0/category", "類別不在允許的值裡。"),
                new ValidationIssue("key.duplicate", "/fields/1/key", "key 重複：impact。"),
            ],
            Truncated: true);
        using var factory = new ApiFactory(FakeBlueprintValidator.Reporting(report));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(route, ApiRequests.Document("{}"));
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);
        var issues = envelope["data"]?["issues"]?.AsArray() ?? [];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(envelope["success"]?.GetValue<bool>(), Is.True);
            Assert.That(envelope["error"], Is.Null);
            Assert.That(envelope["data"]?["valid"]?.GetValue<bool>(), Is.False);
            Assert.That(envelope["data"]?["truncated"]?.GetValue<bool>(), Is.True);
            Assert.That(issues.Select(Describe), Is.EqualTo(new[]
            {
                "structure.enum|/workflows/0/states/0/category|類別不在允許的值裡。",
                "key.duplicate|/fields/1/key|key 重複：impact。",
            }));
        }
    }

    [TestCase(ApiRequests.BlueprintRoute, "ValidateBlueprint")]
    [TestCase(ApiRequests.ChangeSetRoute, "ValidateChangeSet")]
    public async Task Validate_JsonBody_PassesTextAndJsonFormatToMatchingMethod(string route, string expectedMethod)
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();
        const string body = """{"apiVersion":"blueprint/v0","name":"範例"}""";

        // Act
        using var response = await client.PostAsync(route, ApiRequests.Document(body));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(validator.Calls, Is.EqualTo(new[] { new ValidatorCall(expectedMethod, body, DocumentFormat.Json) }));
        }
    }

    [TestCase("application/yaml")]
    [TestCase("application/x-yaml")]
    [TestCase("text/yaml")]
    public async Task Validate_YamlMediaType_PassesYamlFormat(string contentType)
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("key: value", contentType));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(validator.Calls.Select(c => c.Format), Is.EqualTo(new[] { DocumentFormat.Yaml }));
        }
    }

    [TestCase("Application/JSON", DocumentFormat.Json)]
    [TestCase("application/json; charset=utf-8", DocumentFormat.Json)]
    [TestCase("application/json;charset=UTF-8", DocumentFormat.Json)]
    [TestCase("TEXT/YAML; charset=\"utf-8\"", DocumentFormat.Yaml)]
    [TestCase("application/yaml; charset=utf8", DocumentFormat.Yaml)]
    public async Task Validate_MediaTypeWithParametersOrMixedCase_IsAccepted(string contentType, DocumentFormat expected)
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}", contentType));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(validator.Calls.Select(c => c.Format), Is.EqualTo(new[] { expected }));
        }
    }

    [Test]
    public async Task Validate_ExampleYamlFile_PassesFileTextUnchanged()
    {
        // Arrange
        var path = Path.Combine(AppContext.BaseDirectory, "Examples", "agency-project.blueprint.yaml");
        var text = await File.ReadAllTextAsync(path, Encoding.UTF8);
        var validator = FakeBlueprintValidator.Passing();
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document(text, "application/yaml"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(validator.Calls.Single().Text, Is.EqualTo(text));
        }
    }

    [Test]
    public async Task Validate_Utf8ByteOrderMark_IsRemovedBeforeValidation()
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();
        byte[] body = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{}")];

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Bytes(body));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(validator.Calls.Single().Text, Is.EqualTo("{}"));
        }
    }

    [Test]
    public async Task Validate_EmptyBody_PassesEmptyTextToValidator()
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        using var factory = new ApiFactory(validator);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Bytes([]));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(validator.Calls.Single().Text, Is.EqualTo(string.Empty));
        }
    }

    private static string Describe(JsonNode? issue) =>
        $"{issue?["code"]?.GetValue<string>()}|{issue?["path"]?.GetValue<string>()}|{issue?["message"]?.GetValue<string>()}";
}
