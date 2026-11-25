using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Api.Tests.Support;
using PmTool.Storage;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 儲存空間上限的端到端：項目與知識庫的附件加起來算同一個組織的用量，同時上傳也不會一起超過；
/// 兩個模組連的是同一個資料庫，所以這裡才看得出它們共用的鎖有沒有作用。
/// </summary>
[TestFixture]
public sealed class StorageQuotaFlowTests : RealStackTestBase
{
    private const string Project = "QUO";
    private const string SpaceKey = "QKB";
    private const long OrganizationLimit = 100;

    private static readonly BarrierFileStore.Gate Gate = new(TimeSpan.FromSeconds(10));

    // 兩邊都算完「還放得下」才一起往下：沒有共用的鎖時兩邊都會過關，有鎖時後到的根本到不了這裡，等一下就放行。
    private static readonly BarrierFileStore.Gate AfterCheck = new(TimeSpan.FromSeconds(2));

    private SignedInClient _owner = null!;
    private string _itemKey = null!;
    private string _pageRoute = null!;

    internal override RealStackOptions? FactoryOptions => new()
    {
        ConfigureServices = services =>
        {
            // 用量上限調成幾十個位元組，測試才不必真的上傳好幾 GiB；每人每日不限制，只看組織的總量。
            services.RemoveAll<StorageQuotaSettings>();
            services.AddSingleton(new StorageQuotaSettings(OrganizationLimit, 0));
            services.RemoveAll<IStorageQuota>();
            services.AddSingleton<IStorageQuota>(provider => new PausingQuota(
                new StorageQuota(
                    provider.GetServices<IStorageUsageSource>(), provider.GetRequiredService<StorageQuotaSettings>(), provider.GetRequiredService<TimeProvider>()),
                AfterCheck));
            services.RemoveAll<IFileStore>();
            services.AddSingleton<IFileStore>(provider => new BarrierFileStore(FileStores.Create(provider.GetRequiredService<FileStoreSettings>()), Gate));
        },
    };

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(_owner.Client, Project, "simple_tasks");
        _itemKey = (await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "有附件的項目" }))["key"]!.GetValue<string>();
        using var space = await _owner.Client.PostAsync("/v1/knowledge/spaces", ApiRequests.Json(new { key = SpaceKey, name = "知識庫", description = "範例" }));
        await ApiRequests.AssertSuccessEnvelopeAsync(space, HttpStatusCode.Created);
        using var page = await _owner.Client.PostAsync($"/v1/knowledge/spaces/{SpaceKey}/pages", ApiRequests.Json(new { title = "頁面" }));
        var created = await ApiRequests.AssertSuccessEnvelopeAsync(page, HttpStatusCode.Created);
        _pageRoute = $"/v1/knowledge/spaces/{SpaceKey}/pages/{created!["number"]}/attachments";
    }

    [OneTimeTearDown]
    public void DisposeClients() => _owner?.Dispose();

    [Test]
    public async Task AnItemUploadAndAPageUploadAtTheSameTime_ThatTogetherGoOverTheLimit_OnlyOneSucceeds()
    {
        // Arrange：各 60 個位元組、上限 100；兩邊都寫完檔案之後才一起進交易。
        Gate.Arm(writers: 2);
        AfterCheck.Arm(writers: 2);
        var filesBefore = StoredFiles().Length;

        // Act
        var item = _owner.Client.PostAsync($"/v1/items/{_itemKey}/attachments", Upload("item.bin", new byte[60]));
        var page = _owner.Client.PostAsync(_pageRoute, Upload("page.bin", new byte[60]));
        using var itemResponse = await item;
        using var pageResponse = await page;

        // Assert
        var statuses = new[] { itemResponse.StatusCode, pageResponse.StatusCode };
        var rejected = itemResponse.StatusCode == HttpStatusCode.Conflict ? itemResponse : pageResponse;
        var rejectedBody = await rejected.Content.ReadAsStringAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses, Is.EquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
            Assert.That(rejectedBody, Does.Contain("quota_exceeded"));
            Assert.That(StoredFiles(), Has.Length.EqualTo(filesBefore + 1), "被擋下的那一份檔案已經刪掉");
        }
    }

    [Test]
    public async Task TheUsageEndpoint_CountsBothKindsOfAttachments()
    {
        // Arrange
        Gate.Disarm();
        AfterCheck.Disarm();
        var before = await ReadUsageAsync();

        // Act
        using var item = await _owner.Client.PostAsync($"/v1/items/{_itemKey}/attachments", Upload("i.bin", new byte[7]));
        using var page = await _owner.Client.PostAsync(_pageRoute, Upload("p.bin", new byte[5]));
        var after = await ReadUsageAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(after["organizationLimitBytes"]!.GetValue<long>(), Is.EqualTo(OrganizationLimit));
            Assert.That(after["personDailyLimitBytes"], Is.Null);
            Assert.That(AreaBytes(after, "item_attachments") - AreaBytes(before, "item_attachments"), Is.EqualTo(7));
            Assert.That(AreaBytes(after, "knowledge_attachments") - AreaBytes(before, "knowledge_attachments"), Is.EqualTo(5));
            Assert.That(after["totalBytes"]!.GetValue<long>() - before["totalBytes"]!.GetValue<long>(), Is.EqualTo(12));
        }
    }

    private async Task<JsonNode> ReadUsageAsync() => await RealStackSteps.GetAsync(_owner.Client, "/v1/storage-usage");

    private static long AreaBytes(JsonNode usage, string area) =>
        usage["areas"]!.AsArray().Single(a => a!["area"]!.GetValue<string>() == area)!["bytes"]!.GetValue<long>();

    private string[] StoredFiles() => [.. Directory.EnumerateFiles(Factory.AttachmentDirectory, "*", SearchOption.AllDirectories)];

    private static MultipartFormDataContent Upload(string fileName, byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        return new MultipartFormDataContent { { part, "file", fileName } };
    }

    /// <summary>確認交易裡的用量（要寫入的大小大於 0）算完之後，等到兩邊都算完才放行。</summary>
    private sealed class PausingQuota(IStorageQuota inner, BarrierFileStore.Gate gate) : IStorageQuota
    {
        public async Task<StorageQuotaCheck> CheckAsync(Guid organizationId, Guid? personId, long incomingBytes, CancellationToken cancellationToken)
        {
            var check = await inner.CheckAsync(organizationId, personId, incomingBytes, cancellationToken);
            if (incomingBytes > 0)
            {
                await gate.WaitAsync();
            }

            return check;
        }

        public Task<PmTool.Abstractions.Result<StorageUsageReport>> ReportAsync(PmTool.Abstractions.Actor actor, CancellationToken cancellationToken) =>
            inner.ReportAsync(actor, cancellationToken);
    }

    /// <summary>寫完檔案之後等到指定的份數都寫完才放行，讓兩個上傳一定同時走到交易，不靠運氣。</summary>
    private sealed class BarrierFileStore(IFileStore inner, BarrierFileStore.Gate gate) : IFileStore
    {
        public sealed class Gate
        {
            private TaskCompletionSource _open = NewOpen();
            private int _waiting;
            private int _writers;

            public void Arm(int writers)
            {
                _open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _waiting, 0);
                Volatile.Write(ref _writers, writers);
            }

            public void Disarm()
            {
                Volatile.Write(ref _writers, 0);
                _open.TrySetResult();
            }

            public Gate(TimeSpan patience) => _patience = patience;

            private readonly TimeSpan _patience;

            public async Task WaitAsync()
            {
                if (Volatile.Read(ref _writers) == 0)
                {
                    return;
                }

                if (Interlocked.Increment(ref _waiting) >= Volatile.Read(ref _writers))
                {
                    _open.TrySetResult();
                }

                // 另一邊如果在預檢就被擋下、不會來寫檔，這裡不能一直等。
                await Task.WhenAny(_open.Task, Task.Delay(_patience));
            }

            private static TaskCompletionSource NewOpen() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task<StoredFile?> WriteAsync(string key, Stream content, string contentType, long maxBytes, CancellationToken cancellationToken)
        {
            var stored = await inner.WriteAsync(key, content, contentType, maxBytes, cancellationToken);
            await gate.WaitAsync();
            return stored;
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) => inner.OpenReadAsync(key, cancellationToken);

        public Task DeleteAsync(string key, CancellationToken cancellationToken) => inner.DeleteAsync(key, cancellationToken);

        public Task VerifyAsync(CancellationToken cancellationToken) => inner.VerifyAsync(cancellationToken);
    }
}
