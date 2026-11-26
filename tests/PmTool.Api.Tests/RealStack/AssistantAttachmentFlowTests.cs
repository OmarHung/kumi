using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Npgsql;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// AI 助理的附件從 HTTP 一路到資料庫與檔案：上傳（待送）、刪待送的、送出時綁上、對話裡看得到、下載、刪對話之後下載是 404。
/// 送訊息用本機代理的引擎（測試扮演代理領工作、結束），因為這一路不需要真的供應商金鑰；本機引擎只讀得到文字檔，
/// 所以圖片與 PDF 在這裡驗的是「送給本機引擎被擋」。各家 API 模型怎麼送圖片與 PDF 在 AI 模組的測試。
/// </summary>
[TestFixture]
public sealed class AssistantAttachmentFlowTests : RealStackTestBase
{
    private const string Project = "ATTA";

    private static readonly string Conversations = $"/v1/projects/{Project}/assistant/conversations";
    private static readonly string Attachments = $"/v1/projects/{Project}/assistant/attachments";

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1];

    private SignedInClient _owner = null!;
    private HttpClient _agent = null!;
    private Guid _engineId;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(_owner.Client, Project, "simple_tasks");
        await AllowPersonalConnectionsAsync();

        using var created = await _owner.Client.PostAsync(
            "/v1/account/tokens",
            ApiRequests.Json(new { name = "agent", scopes = new[] { "agent:run", "items:read", "blueprint:read" }, expiresInDays = 30 }));
        var secret = (await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created))!["secret"]!.GetValue<string>();
        _agent = Factory.CreateClient();
        _agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        Assert.That(await CheckInAsync(capacity: 0), Is.Not.Null);
        var options = await RealStackSteps.GetAsync(_owner.Client, "/v1/ai/options");
        _engineId = options["connections"]!.AsArray().Single(c => c!["kind"]!.GetValue<string>() == "claude_code")!["id"]!.GetValue<Guid>();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _agent?.Dispose();
        _owner?.Dispose();
    }

    [Test]
    public async Task TextFile_UploadSendDownloadAndDeleteTheConversation_FullFlow()
    {
        // Arrange
        var content = "# 筆記\n- 驗收拆成兩段\n"u8.ToArray();
        var uploaded = await UploadAsync(_owner.Client, "notes.md", content, "text/markdown");
        var attachmentId = uploaded["id"]!.GetValue<Guid>();
        var pendingDownload = await _owner.Client.GetAsync($"{Attachments}/{attachmentId}/content");
        var pendingBytes = await pendingDownload.Content.ReadAsByteArrayAsync();

        // Act：送出時帶上；工作領走時提示裡有檔案的文字。
        var conversationId = await CreateConversationAsync();
        await using var job = await StartJobAsync(conversationId, "請看筆記", [attachmentId]);
        await job.FinishAsync();
        var read = await RealStackSteps.GetAsync(_owner.Client, $"{Conversations}/{conversationId}");
        var userMessage = read["messages"]![0]!;
        using var bound = await _owner.Client.GetAsync($"{Attachments}/{attachmentId}/content");
        var boundBytes = await bound.Content.ReadAsByteArrayAsync();
        using var deleteWhileBound = await _owner.Client.DeleteAsync($"{Attachments}/{attachmentId}");
        using var deleted = await _owner.Client.DeleteAsync($"{Conversations}/{conversationId}");
        using var gone = await _owner.Client.GetAsync($"{Attachments}/{attachmentId}/content");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uploaded["kind"]!.GetValue<string>(), Is.EqualTo("text"));
            Assert.That(uploaded["fileName"]!.GetValue<string>(), Is.EqualTo("notes.md"));
            Assert.That(uploaded["sizeBytes"]!.GetValue<long>(), Is.EqualTo(content.Length));
            Assert.That(pendingDownload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(pendingBytes, Is.EqualTo(content));
            Assert.That(job.Prompt, Does.Contain("驗收拆成兩段"));
            Assert.That(userMessage["attachments"]!.AsArray().Select(a => a!["id"]!.GetValue<Guid>()), Is.EqualTo(new[] { attachmentId }));
            Assert.That(read["messages"]![1]!["attachments"]!.AsArray(), Is.Empty);
            Assert.That(bound.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(boundBytes, Is.EqualTo(content));
            await ApiRequests.AssertErrorEnvelopeAsync(deleteWhileBound, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            await ApiRequests.AssertErrorEnvelopeAsync(gone, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    [Test]
    public async Task Pending_DeleteRemovesIt_AndAnotherPersonCannotSeeIt()
    {
        // Arrange
        var uploaded = await UploadAsync(_owner.Client, "private.txt", "只有我看得到"u8.ToArray(), "text/plain");
        var attachmentId = uploaded["id"]!.GetValue<Guid>();
        using var member = await AddMemberAsync(_owner, Project, "editor", "Editor Example", RealStackSteps.UniqueEmail("attachment-editor"));

        // Act
        using var othersDownload = await member.Client.GetAsync($"{Attachments}/{attachmentId}/content");
        using var othersDelete = await member.Client.DeleteAsync($"{Attachments}/{attachmentId}");
        using var ownDelete = await _owner.Client.DeleteAsync($"{Attachments}/{attachmentId}");
        using var afterDelete = await _owner.Client.GetAsync($"{Attachments}/{attachmentId}/content");
        using var deleteAgain = await _owner.Client.DeleteAsync($"{Attachments}/{attachmentId}");

        // Assert：別人的待送附件一律「找不到」（送訊息時別人的編號也是 422 同一個代碼，那一段在模組的測試）。
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(othersDownload, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
            await ApiRequests.AssertErrorEnvelopeAsync(othersDelete, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
            Assert.That(ownDelete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            await ApiRequests.AssertErrorEnvelopeAsync(afterDelete, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
            await ApiRequests.AssertErrorEnvelopeAsync(deleteAgain, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    [Test]
    public async Task Image_SentToALocalEngine_Is422Unsupported_AndStaysPendingAndNothingIsStored()
    {
        // Arrange
        var uploaded = await UploadAsync(_owner.Client, "截圖.png", PngBytes, "image/png");
        var attachmentId = uploaded["id"]!.GetValue<Guid>();
        var conversationId = await CreateConversationAsync();

        // Act
        using var send = await _owner.Client.PostAsync(
            $"{Conversations}/{conversationId}/messages",
            ApiRequests.Json(new { text = "看圖", selection = new { connectionId = _engineId, model = "default" }, attachmentIds = new[] { attachmentId } }));
        var read = await RealStackSteps.GetAsync(_owner.Client, $"{Conversations}/{conversationId}");
        using var stillThere = await _owner.Client.GetAsync($"{Attachments}/{attachmentId}/content");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(send, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AttachmentUnsupported);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uploaded["kind"]!.GetValue<string>(), Is.EqualTo("image"));
            Assert.That(uploaded["contentType"]!.GetValue<string>(), Is.EqualTo("image/png"));
            Assert.That(read["messages"]!.AsArray(), Is.Empty);
            Assert.That(stillThere.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(stillThere.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
        }
    }

    [Test]
    public async Task Upload_ContentIsJudgedNotTheNameOrDeclaredType()
    {
        // Arrange：副檔名與類型都說是圖片，內容是壓縮檔。
        var zip = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 1, 2, 3, 0xFF, 0xFE };

        // Act
        using var response = await _owner.Client.PostAsync(Attachments, Multipart("看似圖片.png", zip, "image/png"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AttachmentTypeNotAllowed);
    }

    [Test]
    public async Task Upload_AnOversizedTextFile_Is413_AndABadNameIs422()
    {
        // Arrange
        var bigText = new byte[(int)AssistantAttachmentRules.MaxTextBytes + 1];
        Array.Fill(bigText, (byte)'a');

        // Act
        using var tooLarge = await _owner.Client.PostAsync(Attachments, Multipart("big.txt", bigText, "text/plain"));
        using var badName = await _owner.Client.PostAsync(Attachments, Multipart("a/b.txt", [(byte)'x'], "text/plain"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge, AiErrorCodes.AttachmentTooLarge);
        await ApiRequests.AssertFailureEnvelopeAsync(badName, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AttachmentNameInvalid);
    }

    [Test]
    public async Task StorageUsage_CountsTheAssistantAttachments()
    {
        // Arrange
        var before = await AreaBytesAsync("assistant_attachments");

        // Act
        var uploaded = await UploadAsync(_owner.Client, "usage.txt", "12345678"u8.ToArray(), "text/plain");
        var after = await AreaBytesAsync("assistant_attachments");
        using var removed = await _owner.Client.DeleteAsync($"{Attachments}/{uploaded["id"]!.GetValue<Guid>()}");

        // Assert
        Assert.That(after - before, Is.EqualTo(8));
    }

    [Test]
    public async Task Send_WithAnAttachmentIdThatDoesNotExist_Is422NotFound_AndNothingIsStored()
    {
        // Arrange
        var conversationId = await CreateConversationAsync();

        // Act
        using var send = await _owner.Client.PostAsync(
            $"{Conversations}/{conversationId}/messages",
            ApiRequests.Json(new { text = "x", selection = new { connectionId = _engineId, model = "default" }, attachmentIds = new[] { Guid.NewGuid() } }));
        var read = await RealStackSteps.GetAsync(_owner.Client, $"{Conversations}/{conversationId}");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(send, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AttachmentNotFound);
        Assert.That(read["messages"]!.AsArray(), Is.Empty);
    }

    private static MultipartFormDataContent Multipart(string fileName, byte[] bytes, string contentType)
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(part, "file", fileName);
        return content;
    }

    private static async Task<JsonNode> UploadAsync(HttpClient client, string fileName, byte[] bytes, string contentType)
    {
        using var response = await client.PostAsync(Attachments, Multipart(fileName, bytes, contentType));
        return (await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created))!;
    }

    private async Task<long> AreaBytesAsync(string area)
    {
        var usage = await RealStackSteps.GetAsync(_owner.Client, "/v1/storage-usage");
        return usage["areas"]!.AsArray().SingleOrDefault(a => a!["area"]!.GetValue<string>() == area)?["bytes"]?.GetValue<long>() ?? 0;
    }

    private async Task<Guid> CreateConversationAsync(HttpClient? client = null)
    {
        using var created = await (client ?? _owner.Client).PostAsync(Conversations, null);
        return (await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
    }

    // ---- 扮演本機代理 ----

    private async Task<JsonNode?> CheckInAsync(int capacity)
    {
        using var response = await _agent.PostAsync(
            "/v1/agent/check-in",
            ApiRequests.Json(new
            {
                machineKey = "machine-attach",
                machineName = "測試電腦",
                agentVersion = "0.9.0",
                engines = new[] { new { kind = "claude_code", version = "2.1.330", models = new[] { new { id = "default", displayName = "預設", efforts = Array.Empty<string>() } } } },
                capacity,
            }));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
    }

    private async Task<RunningJob> StartJobAsync(Guid conversationId, string text, Guid[] attachmentIds)
    {
        // 回覆是 SSE，要等這件工作結束才讀得完：先放著，領到工作、檢查提示、結束它之後才收。
        var send = _owner.Client.PostAsync(
            $"{Conversations}/{conversationId}/messages",
            ApiRequests.Json(new { text, selection = new { connectionId = _engineId, model = "default" }, attachmentIds }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            var data = await CheckInAsync(capacity: 1);
            if (data?["job"] is JsonObject claimed)
            {
                return new RunningJob(this, send, claimed["jobId"]!.GetValue<Guid>(), claimed["prompt"]!.GetValue<string>());
            }
        }

        throw new TimeoutException("等不到本機代理的工作。");
    }

    private async Task AllowPersonalConnectionsAsync()
    {
        await using var connection = new NpgsqlConnection(Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO ai.settings (organization_id, enabled, allow_personal_connections, updated_at, updated_by)
            SELECT id, false, true, now(), @person FROM identity.organizations
            ON CONFLICT (organization_id) DO UPDATE SET allow_personal_connections = true
            """,
            connection);
        command.Parameters.AddWithValue("person", _owner.PersonId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RunningJob(AssistantAttachmentFlowTests owner, Task<HttpResponseMessage> send, Guid jobId, string prompt) : IAsyncDisposable
    {
        private bool _finished;

        public Guid JobId { get; } = jobId;

        public string Prompt { get; } = prompt;

        public async Task FinishAsync()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            using var finished = await owner._agent.PostAsync($"/v1/agent/jobs/{JobId}/finish", ApiRequests.Json(new { status = "complete" }));
            Assert.That(finished.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            using var response = await send;
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await response.Content.ReadAsStringAsync();
        }

        public async ValueTask DisposeAsync() => await FinishAsync();
    }
}
