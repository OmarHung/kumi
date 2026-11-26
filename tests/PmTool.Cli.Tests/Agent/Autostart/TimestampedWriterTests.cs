using PmTool.Cli.Agent.Autostart;

namespace PmTool.Cli.Tests.Agent.Autostart;

[TestFixture]
public sealed class TimestampedWriterTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 26, 9, 30, 0, TimeSpan.Zero);

    private static string Write(string message)
    {
        using var sink = new StringWriter { NewLine = "\n" };
        new TimestampedWriter(sink, () => At).WriteLine(message);
        return sink.ToString();
    }

    [Test]
    public void WriteLine_PrefixesTheTimeAndEndsWithOneNewline() =>
        Assert.That(Write("已連線"), Is.EqualTo("2026-11-26 09:30:00  已連線\n"));

    [Test]
    public void WriteLine_ReplacesNewlinesSoAMessageCannotForgeAnotherLogLine()
    {
        // Act
        var written = Write("伺服器說：失敗\r\n2026-11-26 09:30:01  已連線，沒有問題\n");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(written.Count(character => character == '\n'), Is.EqualTo(1));
            Assert.That(written, Does.Not.Contain('\r'));
            Assert.That(written, Does.StartWith("2026-11-26 09:30:00  伺服器說：失敗"));
        }
    }

    [Test]
    public void WriteLine_ReplacesOtherControlCharactersToo()
    {
        // Act
        var written = Write("a\u001b[31mred\u0007\u0000b\u0085c\u2028d");

        // Assert
        Assert.That(written, Is.EqualTo("2026-11-26 09:30:00  a [31mred  b c d\n"));
    }

    [Test]
    public async Task WriteLineAsync_BehavesLikeTheSynchronousOne()
    {
        // Arrange
        using var sink = new StringWriter { NewLine = "\n" };
        var writer = new TimestampedWriter(sink, () => At);

        // Act
        await writer.WriteLineAsync("a\nb\u001b");
        await writer.WriteAsync("x\ny");
        await writer.WriteAsync('\u0007');
        await writer.WriteLineAsync();

        // Assert
        Assert.That(sink.ToString(), Is.EqualTo("2026-11-26 09:30:00  a b \nx y \n"));
    }
}
