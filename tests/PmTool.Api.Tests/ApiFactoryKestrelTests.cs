using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>測試用的主機可以改接真的 Kestrel（真的 TCP 連線），給需要真的 socket 的測試用。</summary>
[TestFixture]
public sealed class ApiFactoryKestrelTests
{
    [Test]
    public async Task UseKestrel_Option_ServesRequestsOverARealSocket()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseKestrel = true });
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/health");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(client.BaseAddress?.Port, Is.Not.EqualTo(80), "TestServer 的位址沒有埠號，真的 Kestrel 一定有");
        }
    }
}
