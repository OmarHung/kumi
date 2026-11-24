using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

public sealed class TranslatorSupportTests
{
    [Test]
    public void ToolError_TakesTheMessageOutOfAJsonError()
    {
        // Arrange：Kumi 的 MCP 工具失敗時回的是 {"error": "..."}
        const string text = """{"error":"找不到項目「BUG-1」。"}""";

        // Act
        var error = TranslatorSupport.ToolError(text);

        // Assert
        Assert.That(error, Is.EqualTo("找不到項目「BUG-1」。"));
    }

    [Test]
    public void ToolError_KeepsPlainTextAsIs()
    {
        // Act
        var error = TranslatorSupport.ToolError("狀態 review 不存在。");

        // Assert
        Assert.That(error, Is.EqualTo("狀態 review 不存在。"));
    }

    [Test]
    public void ToolError_StillScrubsTokensInsideAJsonError()
    {
        // Act
        var error = TranslatorSupport.ToolError("""{"error":"Bearer pmt_abc123 沒有權限"}""");

        // Assert
        Assert.That(error, Does.Not.Contain("pmt_abc123"));
    }

    [Test]
    public void ToolError_FallsBackWhenTheJsonHasNoMessage()
    {
        // Act
        var error = TranslatorSupport.ToolError("""{"other":1}""");

        // Assert
        Assert.That(error, Is.EqualTo("""{"other":1}"""));
    }
}
