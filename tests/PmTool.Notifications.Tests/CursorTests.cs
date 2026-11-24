namespace PmTool.Notifications.Tests;

[TestFixture]
public sealed class CursorTests
{
    [Test]
    public void Encode_ThenDecode_ReturnsTheSameValue()
    {
        // Arrange
        var cursor = new NotificationCursor(new DateTimeOffset(2026, 11, 18, 3, 4, 5, TimeSpan.Zero).AddTicks(10), Guid.CreateVersion7());

        // Act
        var decoded = NotificationCursor.TryDecode(cursor.Encode(), out var result);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(decoded, Is.True);
            Assert.That(result, Is.EqualTo(cursor));
        }
    }

    [Test]
    public void Encode_Result_IsUrlSafe()
    {
        // Act
        var text = new NotificationCursor(DateTimeOffset.UtcNow, Guid.CreateVersion7()).Encode();

        // Assert
        Assert.That(text, Does.Match("^[A-Za-z0-9_-]+$"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not a cursor")]
    [TestCase("bjEuMS4x")]
    [TestCase("bjEuOTk5OTk5OTk5OTk5OTk5OTk5OTkuMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDA")]
    [TestCase("bjEuLTUuMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDA")]
    public void TryDecode_Garbage_ReturnsFalse(string? text)
    {
        // Act
        var decoded = NotificationCursor.TryDecode(text, out _);

        // Assert
        Assert.That(decoded, Is.False);
    }
}
