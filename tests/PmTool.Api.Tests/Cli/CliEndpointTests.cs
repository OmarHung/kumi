using System.Net;
using System.Net.Http.Headers;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests.Cli;

/// <summary>GET /v1/cli 與 GET /v1/cli/download/{platform}。</summary>
[TestFixture]
public sealed class CliEndpointTests
{
    private const string ArmContent = "apple-silicon-binary-0123456789";
    private const string WindowsContent = "windows-binary";

    private string _packages = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _packages = ApiFactory.CreateTemporaryDirectory();
        File.WriteAllText(Path.Combine(_packages, "kumi-0.3.0-macos-arm64"), ArmContent);
        File.WriteAllText(Path.Combine(_packages, "kumi-0.3.0-windows-x64.exe"), WindowsContent);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_packages))
        {
            Directory.Delete(_packages, recursive: true);
        }
    }

    [Test]
    public async Task List_SignedInWithTheCookie_ReturnsTheLatestVersionAndThePackagesThatExist()
    {
        // Arrange
        using var factory = Factory(_packages);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/cli");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var packages = data!["packages"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data["version"]!.GetValue<string>(), Is.EqualTo("0.3.0"));
            Assert.That(packages.Select(p => p!["platform"]!.GetValue<string>()), Is.EqualTo(new[] { "macos-arm64", "windows-x64" }));
            Assert.That(packages[0]!["fileName"]!.GetValue<string>(), Is.EqualTo("kumi-0.3.0-macos-arm64"));
            Assert.That(packages[0]!["sizeBytes"]!.GetValue<long>(), Is.EqualTo(ArmContent.Length));
            Assert.That(packages[0]!["sha256"]!.GetValue<string>(), Is.EqualTo(CliPackageCatalogTests.Sha256Of(ArmContent)));
            Assert.That(packages[0]!.AsObject().ContainsKey("filePath"), Is.False);
        }
    }

    [TestCase(AccessTokenScope.ItemsRead)]
    [TestCase(AccessTokenScope.BlueprintWrite)]
    [TestCase(AccessTokenScope.AgentRun)]
    public async Task List_AnyTokenScope_IsAllowed(AccessTokenScope scope)
    {
        // Arrange
        using var factory = Factory(_packages);
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, scope);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/cli", secret));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Download_TokenWithOnlyTheAgentScope_IsAllowed()
    {
        // Arrange
        using var factory = Factory(_packages);
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.AgentRun);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(
            AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/cli/download/windows-x64", secret));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(WindowsContent));
        }
    }

    [TestCase("/v1/cli")]
    [TestCase("/v1/cli/download/macos-arm64")]
    public async Task Endpoints_NotSignedIn_Return401(string route)
    {
        // Arrange
        using var factory = Factory(_packages);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task List_NothingIsProvided_ReturnsANullVersionAndNoPackages()
    {
        // Arrange
        using var factory = Factory(Path.Combine(_packages, "missing"));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/cli");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data!.AsObject().ContainsKey("version") && data["version"] is null, Is.True);
            Assert.That(data["packages"]!.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task Download_KnownPlatform_ReturnsTheFileWithTheDownloadHeaders()
    {
        // Arrange
        using var factory = Factory(_packages);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/cli/download/macos-arm64");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(ArmContent));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(ArmContent.Length));
            Assert.That(response.Content.Headers.ContentDisposition?.ToString(), Does.StartWith("attachment;"));
            Assert.That(response.Content.Headers.ContentDisposition?.FileName, Is.EqualTo("kumi-0.3.0-macos-arm64"));
            Assert.That(Header(response, "X-Content-Type-Options"), Is.EqualTo("nosniff"));
            Assert.That(Header(response, "Content-Security-Policy"), Is.EqualTo("default-src 'none'; sandbox"));
            Assert.That(response.Headers.CacheControl?.Private, Is.True);
            Assert.That(response.Headers.CacheControl?.MaxAge, Is.EqualTo(TimeSpan.Zero));
            Assert.That(Header(response, "X-Kumi-Sha256"), Is.EqualTo(CliPackageCatalogTests.Sha256Of(ArmContent)));
        }
    }

    [Test]
    public async Task Download_RangeRequest_Returns206WithThatPartOfTheFile()
    {
        // Arrange
        using var factory = Factory(_packages);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/cli/download/macos-arm64")
        {
            Headers = { Range = new RangeHeaderValue(6, 12) },
        };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(ArmContent[6..13]));
            Assert.That(response.Content.Headers.ContentRange?.ToString(), Is.EqualTo($"bytes 6-12/{ArmContent.Length}"));
        }
    }

    [TestCase("macos-x64")]
    [TestCase("linux-x64")]
    [TestCase("..%2Fkumi-0.3.0-macos-arm64")]
    [TestCase("MACOS-ARM64")]
    public async Task Download_UnknownPlatformOrNoFileForIt_Returns404WithItsOwnCode(string platform)
    {
        // Arrange
        using var factory = Factory(_packages);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"/v1/cli/download/{platform}");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, "cli.package_not_found");
    }

    [Test]
    public async Task Download_NothingIsProvided_Returns404()
    {
        // Arrange
        using var factory = Factory(Path.Combine(_packages, "missing"));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/cli/download/macos-arm64");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, "cli.package_not_found");
    }

    private static ApiFactory Factory(string packagesPath) => new(
        FakeBlueprintValidator.Passing(),
        new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Cli:PackagesPath"] = packagesPath } });

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single() : null;
}
