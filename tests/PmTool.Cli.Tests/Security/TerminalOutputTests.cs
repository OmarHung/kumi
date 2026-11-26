using PmTool.Cli.Output;

namespace PmTool.Cli.Tests.Security;

[TestFixture]
public sealed class TerminalOutputTests
{
    [TestCase("a\u202Eb", "ab", TestName = "方向覆寫")]
    [TestCase("a\u200Bb\u200Dc\uFEFF", "abc", TestName = "零寬與位元組順序記號")]
    [TestCase("x\u2066y\u2069", "xy", TestName = "方向隔離")]
    [TestCase("t\U000E0041\U000E007F", "t", TestName = "補充平面的標籤字元")]
    [TestCase("正常的標題 ok", "正常的標題 ok", TestName = "一般文字不動")]
    [TestCase("emoji \U0001F600 ok", "emoji \U0001F600 ok", TestName = "補充平面的一般字元不動")]
    public void Clean_DropsFormatCharacters(string input, string expected) =>
        Assert.That(SafeTextWriter.Clean(input), Is.EqualTo(expected));

    [TestCase("a\rb", "a b", TestName = "單獨的歸位換成空白")]
    [TestCase("a\r\nb", "a\r\nb", TestName = "歸位加換行照舊")]
    [TestCase("line\r", "line ", TestName = "結尾的歸位")]
    [TestCase("a\r\rb\r\n", "a  b\r\n", TestName = "連續的歸位只有接著換行的那個保留")]
    public void Clean_ReplacesALoneCarriageReturnWithASpace(string input, string expected) =>
        Assert.That(SafeTextWriter.Clean(input), Is.EqualTo(expected));

    [Test]
    public void Clean_ALoneSurrogateIsDropped_AndAPairIsKept()
    {
        // Act
        var result = SafeTextWriter.Clean("a\uD83Db\U0001F600");

        // Assert
        Assert.That(result, Is.EqualTo("ab\U0001F600"));
    }

    [Test]
    public void Writer_CharByChar_DropsASupplementaryFormatCharacterAndKeepsAnEmoji()
    {
        // Arrange
        var inner = new StringWriter();
        var writer = new SafeTextWriter(inner);

        // Act
        foreach (var character in "t\U000E0041x\U0001F600y\uD83Dz")
        {
            writer.Write(character);
        }

        foreach (var character in "\U000E007Fq")
        {
            writer.WriteAsync(character).GetAwaiter().GetResult();
        }

        // Assert
        Assert.That(inner.ToString(), Is.EqualTo("tx\U0001F600yzq"));
    }

    [Test]
    public void Writer_FiltersEveryWritePath()
    {
        // Arrange
        var inner = new StringWriter();
        var writer = new SafeTextWriter(inner);

        // Act
        writer.Write("a\u202Eb");
        writer.Write('\u200B');
        writer.Write('\r');
        writer.Write(['c', '\u2066', 'd'], 0, 3);
        writer.WriteAsync("e\rf".AsMemory()).GetAwaiter().GetResult();
        writer.WriteLineAsync("g\u202Ah").GetAwaiter().GetResult();

        // Assert
        Assert.That(inner.ToString(), Is.EqualTo("ab cde fgh" + Environment.NewLine));
    }

    [Test]
    public async Task Command_PrintsAServerTitleWithoutDirectionControlOrFakeLines()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/session", new
        {
            user = new { displayName = "王\u202E小明\rFAKE", email = "ming@example.com", organizationName = "範例公司", role = "member" },
        });

        // Act
        var result = await cli.RunAsync("auth", "status");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out.Contains((char)0x202E), Is.False, "NUnit 的 Contain 用文化比較，會忽略這類字元");
            Assert.That(result.Out.Replace("\r\n", "\n").Contains((char)13), Is.False);
            Assert.That(result.Out, Does.Contain("王小明 FAKE"));
        }
    }
}
