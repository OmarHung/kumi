using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Npgsql;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 本機代理收到 AI 助理訊息的圖片與 PDF（internal/HANDOFF-ai-attachments.md 第 8 節）：上傳 → 送給 0.10.0 的本機引擎 →
/// 領工作的回應帶 <c>attachments</c> → 用工作的端點下載，內容與 SHA-256 對得上；不在清單裡的、別人的、結束之後的、Cookie 的都下載不到。
/// 舊版 kumi（0.9.0）送圖片是 422，請使用者更新。
/// </summary>
[TestFixture]
public sealed class AgentAttachmentFlowTests : RealStackTestBase
{
    private const string Project = "ATTC";

    private static readonly string Conversations = $"/v1/projects/{Project}/assistant/conversations";
    private static readonly string Attachments = $"/v1/projects/{Project}/assistant/attachments";

    // 1x1 的 PNG 檔頭：夠過內容判斷（寬高讀得出來、沒超過上限）。
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 0x1F, 0x15, 0xC4, 0x89];

    private static readonly byte[] PdfBytes = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    private SignedInClient _owner = null!;
    private HttpClient _agent = null!;
    private Guid _engineId;
    private string _agentVersion = "0.10.0";

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
    public async Task ImageAndPdf_ReachTheLocalAgentAndDownloadWithAMatchingHash()
    {
        // Arrange
        _agentVersion = "0.10.0";
        await CheckInAsync(capacity: 0);
        var image = await UploadAsync("截圖.png", PngBytes, "image/png");
        var pdf = await UploadAsync("規格.pdf", PdfBytes, "application/pdf");
        var unlisted = await UploadAsync("別則的.png", PngBytes, "image/png");
        var conversationId = await CreateConversationAsync();

        // Act
        await using var job = await StartJobAsync(conversationId, "請看圖和規格", [image, pdf]);
        var attachments = job.Node["attachments"]!.AsArray();
        var imageDownload = await _agent.GetAsync($"/v1/agent/jobs/{job.JobId}/attachments/{image}");
        var imageBytes = await imageDownload.Content.ReadAsByteArrayAsync();
        using var pdfDownload = await _agent.GetAsync($"/v1/agent/jobs/{job.JobId}/attachments/{pdf}");
        var pdfBytes = await pdfDownload.Content.ReadAsByteArrayAsync();
        using var notListed = await _agent.GetAsync($"/v1/agent/jobs/{job.JobId}/attachments/{unlisted}");
        using var withCookie = await _owner.Client.GetAsync($"/v1/agent/jobs/{job.JobId}/attachments/{image}");
        await job.FinishAsync();
        using var afterFinish = await _agent.GetAsync($"/v1/agent/jobs/{job.JobId}/attachments/{image}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attachments, Has.Count.EqualTo(2));
            Assert.That(attachments[0]!["id"]!.GetValue<Guid>(), Is.EqualTo(image));
            Assert.That(attachments[0]!["fileName"]!.GetValue<string>(), Is.EqualTo("截圖.png"));
            Assert.That(attachments[0]!["contentType"]!.GetValue<string>(), Is.EqualTo("image/png"));
            Assert.That(attachments[0]!["kind"]!.GetValue<string>(), Is.EqualTo("image"));
            Assert.That(attachments[0]!["sizeBytes"]!.GetValue<long>(), Is.EqualTo(PngBytes.Length));
            Assert.That(attachments[0]!["sha256"]!.GetValue<string>(), Is.EqualTo(Sha256Of(PngBytes)));
            Assert.That(attachments[1]!["kind"]!.GetValue<string>(), Is.EqualTo("pdf"));
            Assert.That(job.Prompt, Does.Contain("〔附件 1：截圖.png（附在這則訊息的最後）〕"));
            Assert.That(job.Prompt, Does.Contain("〔附件 2：規格.pdf（附在這則訊息的最後）〕"));
            Assert.That(imageDownload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(imageDownload.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(imageDownload.Headers.GetValues("X-Attachment-Sha256").Single(), Is.EqualTo(Sha256Of(PngBytes)));
            Assert.That(imageBytes, Is.EqualTo(PngBytes));
            Assert.That(Sha256Of(imageBytes), Is.EqualTo(attachments[0]!["sha256"]!.GetValue<string>()));
            Assert.That(pdfBytes, Is.EqualTo(PdfBytes));
            Assert.That(Sha256Of(pdfBytes), Is.EqualTo(attachments[1]!["sha256"]!.GetValue<string>()));
            await ApiRequests.AssertErrorEnvelopeAsync(notListed, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
            Assert.That(withCookie.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            await ApiRequests.AssertErrorEnvelopeAsync(afterFinish, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    [Test]
    public async Task OldKumi_ImageIsRefusedWithAnUpdateHint_AndTheAttachmentStaysPending()
    {
        // Arrange
        _agentVersion = "0.9.0";
        await CheckInAsync(capacity: 0);
        var image = await UploadAsync("截圖.png", PngBytes, "image/png");
        var conversationId = await CreateConversationAsync();

        // Act
        using var send = await _owner.Client.PostAsync(
            $"{Conversations}/{conversationId}/messages",
            ApiRequests.Json(new { text = "看圖", selection = new { connectionId = _engineId, model = "default" }, attachmentIds = new[] { image } }));
        using var stillPending = await _owner.Client.GetAsync($"{Attachments}/{image}/content");
        var body = await send.Content.ReadAsStringAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(send.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(body, Does.Contain(AiErrorCodes.AttachmentUnsupported));
            Assert.That(body, Does.Contain("kumi update"));
            Assert.That(stillPending.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static MultipartFormDataContent Multipart(string fileName, byte[] bytes, string contentType)
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(part, "file", fileName);
        return content;
    }

    private async Task<Guid> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        using var response = await _owner.Client.PostAsync(Attachments, Multipart(fileName, bytes, contentType));
        return (await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
    }

    private async Task<Guid> CreateConversationAsync()
    {
        using var created = await _owner.Client.PostAsync(Conversations, null);
        return (await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
    }

    // ---- 扮演本機代理 ----

    private async Task<JsonNode?> CheckInAsync(int capacity)
    {
        using var response = await _agent.PostAsync(
            "/v1/agent/check-in",
            ApiRequests.Json(new
            {
                machineKey = "machine-agent-attach",
                machineName = "測試電腦",
                agentVersion = _agentVersion,
                engines = new[] { new { kind = "claude_code", version = "2.1.330", models = new[] { new { id = "default", displayName = "預設", efforts = Array.Empty<string>() } } } },
                capacity,
            }));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
    }

    private async Task<RunningJob> StartJobAsync(Guid conversationId, string text, Guid[] attachmentIds)
    {
        // 回覆是 SSE，要等這件工作結束才讀得完：先放著，領到工作、檢查、結束它之後才收。
        var send = _owner.Client.PostAsync(
            $"{Conversations}/{conversationId}/messages",
            ApiRequests.Json(new { text, selection = new { connectionId = _engineId, model = "default" }, attachmentIds }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            var data = await CheckInAsync(capacity: 1);
            if (data?["job"] is JsonObject claimed)
            {
                return new RunningJob(this, send, claimed);
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

    private sealed class RunningJob(AgentAttachmentFlowTests owner, Task<HttpResponseMessage> send, JsonObject node) : IAsyncDisposable
    {
        private bool _finished;

        public JsonObject Node { get; } = node;

        public Guid JobId { get; } = node["jobId"]!.GetValue<Guid>();

        public string Prompt { get; } = node["prompt"]!.GetValue<string>();

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
