using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Google.Cloud.Storage.V1;

namespace PmTool.Storage.Tests;

/// <summary>
/// GCS 的儲存，對 fsouza/fake-gcs-server（GCS 的模擬伺服器）跑：可續傳上傳、串流下載、刪除、啟動檢查。
/// 模擬伺服器要知道外面連它的網址（可續傳上傳的 Location 標頭），所以容器的連接埠先挑好、不用隨機的。
/// </summary>
[TestFixture]
public sealed class GcsFileStoreTests
{
    private const string Bucket = "attachments-test";
    private const int ContainerPort = 4443;

    private IContainer _container = null!;
    private StorageClient _client = null!;
    private GcsFileStore _store = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var hostPort = FreePort();
        _container = new ContainerBuilder("fsouza/fake-gcs-server:1.52.2")
            .WithCommand("-scheme", "http", "-port", ContainerPort.ToString(), "-external-url", $"http://localhost:{hostPort}")
            .WithPortBinding(hostPort, ContainerPort)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ContainerPort).ForPath("/storage/v1/b")))
            .Build();
        await _container.StartAsync();

        _client = new StorageClientBuilder
        {
            BaseUri = $"http://localhost:{hostPort}/storage/v1/",
            UnauthenticatedAccess = true,
        }.Build();
        await _client.CreateBucketAsync("example-project", Bucket);
        _store = new GcsFileStore(_client, Bucket);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        _client?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Test]
    public async Task WriteAsync_ThenOpenReadAsync_ReturnsTheSameBytesAndTheSizeAndSha256()
    {
        // Arrange：比一個上傳區塊（4 MiB）大，走到多個區塊的可續傳上傳。
        var key = Guid.CreateVersion7().ToString("D");
        var bytes = new byte[9 * 1024 * 1024 + 123];
        new Random(42).NextBytes(bytes);

        // Act
        var stored = await _store.WriteAsync(key, new MemoryStream(bytes), "application/octet-stream", 25 * 1024 * 1024, CancellationToken.None);
        await using var read = await _store.OpenReadAsync(key, CancellationToken.None);
        using var copy = new MemoryStream();
        await read!.CopyToAsync(copy);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored, Is.EqualTo(new StoredFile(bytes.Length, StorageSupport.Sha256Of(bytes))));
            Assert.That(copy.ToArray(), Is.EqualTo(bytes));
        }
    }

    [Test]
    public async Task WriteAsync_AnEmptyFile_IsStoredAndReadBackEmpty()
    {
        // Arrange
        var key = Guid.CreateVersion7().ToString("D");

        // Act
        var stored = await _store.WriteAsync(key, new MemoryStream([]), "text/plain", 1024, CancellationToken.None);
        await using var read = await _store.OpenReadAsync(key, CancellationToken.None);
        using var copy = new MemoryStream();
        await read!.CopyToAsync(copy);

        // Assert
        Assert.That((stored, copy.Length), Is.EqualTo((new StoredFile(0, StorageSupport.Sha256Of([])), 0L)));
    }

    [Test]
    public async Task WriteAsync_OverTheLimit_ReturnsNullAndLeavesNoObject()
    {
        // Arrange
        var key = Guid.CreateVersion7().ToString("D");

        // Act
        var stored = await _store.WriteAsync(key, new StorageSupport.ZeroStream(6 * 1024 * 1024), "application/zip", 5 * 1024 * 1024, CancellationToken.None);
        var read = await _store.OpenReadAsync(key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored, Is.Null);
            Assert.That(read, Is.Null);
        }
    }

    [Test]
    public async Task OpenReadAsync_AMissingObject_IsNull()
    {
        // Act
        var read = await _store.OpenReadAsync(Guid.CreateVersion7().ToString("D"), CancellationToken.None);

        // Assert
        Assert.That(read, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_RemovesTheObjectAndAMissingObjectIsNotAnError()
    {
        // Arrange
        var key = Guid.CreateVersion7().ToString("D");
        await _store.WriteAsync(key, new MemoryStream([1, 2, 3]), "application/octet-stream", 1024, CancellationToken.None);

        // Act
        await _store.DeleteAsync(key, CancellationToken.None);
        await _store.DeleteAsync(key, CancellationToken.None);
        var read = await _store.OpenReadAsync(key, CancellationToken.None);

        // Assert
        Assert.That(read, Is.Null);
    }

    [Test]
    public async Task OpenReadAsync_DisposingBeforeTheEnd_DoesNotHangOrFail()
    {
        // Arrange
        var key = Guid.CreateVersion7().ToString("D");
        await _store.WriteAsync(key, new StorageSupport.ZeroStream(3 * 1024 * 1024), "application/zip", 25 * 1024 * 1024, CancellationToken.None);

        // Act
        var read = await _store.OpenReadAsync(key, CancellationToken.None);
        var buffer = new byte[1024];
        _ = await read!.ReadAsync(buffer);

        // Assert
        Assert.That(async () => await read.DisposeAsync(), Throws.Nothing);
    }

    [Test]
    public void VerifyAsync_AnExistingBucket_Passes()
    {
        // Act / Assert
        Assert.That(async () => await _store.VerifyAsync(CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public void VerifyAsync_AMissingBucket_FailsAndNamesTheSetting()
    {
        // Arrange
        var store = new GcsFileStore(_client, "no-such-bucket");

        // Act / Assert
        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => store.VerifyAsync(CancellationToken.None));
        Assert.That(failure.Message, Does.Contain("Attachments:Bucket").And.Contain("no-such-bucket"));
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
