using PmTool.Cli.Login;

namespace PmTool.Cli.Tests.Login;

[TestFixture]
public sealed class ConfirmationCodeTests
{
    [TestCase("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "951123")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "941901")]
    [TestCase("abcdefghijklmnopqrstuvwxyz0123456789-_ABCDEFG", "342122")]
    public void Of_KnownChallenge_GivesTheAgreedCode(string challenge, string expected)
    {
        // Act
        var code = ConfirmationCode.Of(challenge);

        // Assert
        Assert.That(code, Is.EqualTo(expected));
    }

    [Test]
    public void Display_SplitsTheCodeIntoTwoGroupsOfThree()
    {
        // Act
        var shown = ConfirmationCode.Display("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");

        // Assert
        Assert.That(shown, Is.EqualTo("951 123"));
    }
}
