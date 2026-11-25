using System.Net;
using Google;
using Google.Cloud.Storage.V1;

namespace PmTool.Storage.Tests;

/// <summary>GCS 的鍵已經有物件時：不覆寫、也不能刪掉別人的物件；不合格的鍵一律擋下。不連真的伺服器。</summary>
[TestFixture]
public sealed class GcsExistingObjectTests
{
    private sealed class FakeClient : StorageClient
    {
        public HttpStatusCode? UploadStatus { get; init; }

        /// <summary>true 時把狀態碼的例外再包一層才丟出去。</summary>
        public bool WrapFailure { get; init; }

        public UploadObjectOptions? LastOptions { get; private set; }

        public List<string> Deleted { get; } = [];

        public override Task<Google.Apis.Storage.v1.Data.Object> UploadObjectAsync(
            string bucket,
            string objectName,
            string? contentType,
            Stream source,
            UploadObjectOptions? options = null,
            CancellationToken cancellationToken = default,
            IProgress<Google.Apis.Upload.IUploadProgress>? progress = null)
        {
            LastOptions = options;
            if (UploadStatus is { } status)
            {
                var failure = new GoogleApiException("Storage", "條件不符") { HttpStatusCode = status };
                throw WrapFailure ? new InvalidOperationException("上傳失敗", failure) : failure;
            }

            return Task.FromResult(new Google.Apis.Storage.v1.Data.Object { Name = objectName });
        }

        public override Task DeleteObjectAsync(
            string bucket, string objectName, DeleteObjectOptions? options = null, CancellationToken cancellationToken = default)
        {
            Deleted.Add(objectName);
            return Task.CompletedTask;
        }
    }

    [Test]
    public void WriteAsync_WhenTheKeyAlreadyHasAnObject_FailsWithoutDeletingIt()
    {
        // Arrange
        var client = new FakeClient { UploadStatus = HttpStatusCode.PreconditionFailed };
        var store = new GcsFileStore(client, "attachments-test");
        var key = Guid.CreateVersion7().ToString("D");

        // Act
        var failure = Assert.ThrowsAsync<InvalidOperationException>(
            () => store.WriteAsync(key, new MemoryStream([1]), "text/plain", 1024, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Message, Does.Contain("已經存在"));
            Assert.That(client.Deleted, Is.Empty);
        }
    }

    [Test]
    public void WriteAsync_WhenTheExistingObjectFailureIsWrapped_StillFailsWithoutDeletingIt()
    {
        // Arrange
        var client = new FakeClient { UploadStatus = HttpStatusCode.PreconditionFailed, WrapFailure = true };
        var store = new GcsFileStore(client, "attachments-test");
        var key = Guid.CreateVersion7().ToString("D");

        // Act
        var failure = Assert.ThrowsAsync<InvalidOperationException>(
            () => store.WriteAsync(key, new MemoryStream([1]), "text/plain", 1024, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Message, Does.Contain("已經存在"));
            Assert.That(client.Deleted, Is.Empty);
        }
    }

    [Test]
    public async Task WriteAsync_AlwaysAsksForANewObject()
    {
        // Arrange
        var client = new FakeClient();
        var store = new GcsFileStore(client, "attachments-test");

        // Act
        await store.WriteAsync(Guid.CreateVersion7().ToString("D"), new MemoryStream([1]), "text/plain", 1024, CancellationToken.None);

        // Assert
        Assert.That(client.LastOptions?.IfGenerationMatch, Is.EqualTo(0));
    }

    [Test]
    public void WriteAsync_AnyOtherFailure_StillRemovesTheHalfWrittenObject()
    {
        // Arrange
        var client = new FakeClient { UploadStatus = HttpStatusCode.InternalServerError };
        var store = new GcsFileStore(client, "attachments-test");
        var key = Guid.CreateVersion7().ToString("D");

        // Act
        Assert.ThrowsAsync<GoogleApiException>(() => store.WriteAsync(key, new MemoryStream([1]), "text/plain", 1024, CancellationToken.None));

        // Assert
        Assert.That(client.Deleted, Is.EqualTo(new[] { key }));
    }

    [TestCase("../etc/passwd")]
    [TestCase("a/b")]
    [TestCase("not-a-guid")]
    [TestCase("")]
    public void Keys_ThatAreNotIds_AreRefusedByEveryOperation(string key)
    {
        // Arrange
        var client = new FakeClient();
        var store = new GcsFileStore(client, "attachments-test");

        // Act / Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(key, new MemoryStream([1]), "text/plain", 1024, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync(key, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(key, CancellationToken.None));
            Assert.That(client.Deleted, Is.Empty);
        }
    }
}
