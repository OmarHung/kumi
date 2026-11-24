using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class ErrorTextTests
{
    [TestCase("failed to read /Users/someone/.config/kumi/config.json now", "failed to read … now")]
    [TestCase("open ~/.codex/auth.json failed", "open … failed")]
    [TestCase(@"cannot open C:\Users\someone\kumi\config.json", "cannot open …")]
    [TestCase(@"open \\fileserver\share\kumi\x.txt failed", "open … failed")]
    [TestCase("token pmt_abcDEF_123-x leaked", "token … leaked")]
    [TestCase("Authorization: Bearer abc.def.ghi sent", "Authorization: Bearer … sent")]
    public void Sanitize_HidesTokensAndPaths(string input, string expected)
    {
        // Act
        var result = ErrorText.Sanitize(input);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Sanitize_LeavesUrlsAndShortPathsAlone()
    {
        // Act
        var result = ErrorText.Sanitize("連不上 https://pm.example.com/mcp/health，路徑 /mcp 不對");

        // Assert
        Assert.That(result, Is.EqualTo("連不上 https://pm.example.com/mcp/health，路徑 /mcp 不對"));
    }

    [Test]
    public void Sanitize_HidesGivenLiteralsAndTheHomeFolder()
    {
        // Arrange
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Act
        var result = ErrorText.Sanitize($"secret-value-123 in {home}", "secret-value-123");

        // Assert
        Assert.That(result, Is.EqualTo("… in …"));
    }

    [Test]
    public void Sanitize_ReplacesControlCharactersAndCapsTheLength()
    {
        // Act
        var result = ErrorText.Sanitize("a\u0000b\tc" + new string('x', 700));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.StartWith("a b c"));
            Assert.That(result, Has.Length.EqualTo(ErrorText.MaxLength));
            Assert.That(result, Does.EndWith("…"));
        }
    }

    [Test]
    public void FirstParagraph_TakesTheTextBeforeTheFirstBlankLine()
    {
        // Act
        var result = ErrorText.FirstParagraph("原因一行\n第二行\n\n用法說明");

        // Assert
        Assert.That(result, Is.EqualTo("原因一行\n第二行"));
    }

    [Test]
    public void Sanitize_OfNothing_IsEmpty()
    {
        Assert.That(ErrorText.Sanitize("  "), Is.Empty);
        Assert.That(ErrorText.Sanitize(null), Is.Empty);
    }
}
