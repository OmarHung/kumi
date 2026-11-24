
namespace PmTool.Storage.Tests;

/// <summary>邊讀邊算大小與 SHA-256，超過上限時中止。</summary>
[TestFixture]
public sealed class MeasuringStreamTests
{
    [Test]
    public async Task ReadingToTheEnd_CountsTheBytesAndHashesThem()
    {
        // Arrange
        var bytes = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 7)).ToArray();
        await using var measured = new MeasuringStream(new MemoryStream(bytes), maxBytes: 200_000);

        // Act
        await measured.CopyToAsync(Stream.Null);

        // Assert
        Assert.That(measured.Complete(), Is.EqualTo(new StoredFile(200_000, StorageSupport.Sha256Of(bytes))));
    }

    [Test]
    public async Task ReadingOneByteMoreThanTheLimit_Throws()
    {
        // Arrange
        await using var measured = new MeasuringStream(new MemoryStream(new byte[1001]), maxBytes: 1000);

        // Act / Assert
        Assert.That(async () => await measured.CopyToAsync(Stream.Null), Throws.TypeOf<FileTooLargeException>());
    }

    [Test]
    public void SynchronousReads_AreMeasuredToo()
    {
        // Arrange
        using var measured = new MeasuringStream(new MemoryStream(new byte[10]), maxBytes: 5);
        var buffer = new byte[16];

        // Act / Assert
        Assert.That(() => measured.Read(buffer, 0, buffer.Length), Throws.TypeOf<FileTooLargeException>());
    }

    [Test]
    public async Task AnEmptyStream_HasTheSha256OfNothing()
    {
        // Arrange
        await using var measured = new MeasuringStream(Stream.Null, maxBytes: 10);

        // Act
        await measured.CopyToAsync(Stream.Null);

        // Assert
        Assert.That(measured.Complete(), Is.EqualTo(new StoredFile(0, StorageSupport.Sha256Of([]))));
    }
}
