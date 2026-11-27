using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Npgsql;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 不選專案的組織對話的附件從 HTTP 一路到資料庫與檔案：上傳到 <c>/v1/assistant/attachments</c>、送出時綁上、引擎讀得到、
/// 對話裡看得到、下載；用專案的路徑讀不到；刪對話之後下載是 404。送訊息用本機代理的引擎（測試扮演代理），組織對話要 kumi 0.12.0 以上。
/// </summary>
[TestFixture]
public sealed class OrganizationAttachmentFlowTests : RealStackTestBase
{
    private const string Project = "ORGA";
    private const string Conversations = "/v1/assistant/conversations";
    private const string Attachments = "/v1/assistant/attachments";
    private const string ProjectAttachments = $"/v1/projects/{Project}/assistant/attachments";

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
        var content = "# 需求\n- 報名網站，下個月上線\n"u8.ToArray();
        var attachmentId = await UploadAsync("需求.md", content, "text/markdown");
        using var viaProjectWhilePending = await _owner.Client.GetAsync($"{ProjectAttachments}/{attachmentId}/content");

        // Act：送出時帶上；工作領走時提示裡有檔案的文字。
        using var createdConversation = await _owner.Client.PostAsync(Conversations, null);
        var conversationId = (await ApiRequests.AssertSuccessEnvelopeAsync(createdConversation, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
        var prompt = await SendAndFinishAsync(conversationId, "照這份需求開專案", attachmentId);
        var read = await RealStackSteps.GetAsync(_owner.Client, $"{Conversations}/{conversationId}");
        using var bound = await _owner.Client.GetAsync($"{Attachments}/{attachmentId}/content");
        var boundBytes = await bound.Content.ReadAsByteArrayAsync();
        using var deleted = await _owner.Client.DeleteAsync($"{Conversations}/{conversationId}");
        using var gone = await _owner.Client.GetAsync($"{Attachments}/{attachmentId}/content");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(viaProjectWhilePending, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
            Assert.That(prompt, Does.Contain("報名網站，下個月上線"));
            Assert.That(read["messages"]![0]!["attachments"]!.AsArray().Select(a => a!["id"]!.GetValue<Guid>()), Is.EqualTo(new[] { attachmentId }));
            Assert.That(bound.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(boundBytes, Is.EqualTo(content));
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            await ApiRequests.AssertErrorEnvelopeAsync(gone, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    [Test]
    public async Task Pending_DeleteRemovesIt_AndTheProjectPathCannotDeleteIt()
    {
        // Arrange
        var attachmentId = await UploadAsync("notes.txt", "只有我看得到"u8.ToArray(), "text/plain");

        // Act
        using var viaProject = await _owner.Client.DeleteAsync($"{ProjectAttachments}/{attachmentId}");
        using var ownDelete = await _owner.Client.DeleteAsync($"{Attachments}/{attachmentId}");
        using var afterDelete = await _owner.Client.GetAsync($"{Attachments}/{attachmentId}/content");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(viaProject, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
            Assert.That(ownDelete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            await ApiRequests.AssertErrorEnvelopeAsync(afterDelete, HttpStatusCode.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    private async Task<Guid> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(part, "file", fileName);
        using var response = await _owner.Client.PostAsync(Attachments, content);
        return (await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
    }

    /// <summary>送出（回覆是 SSE，要等工作結束才讀得完）、扮演代理領工作並結束它，回傳工作的提示。</summary>
    private async Task<string> SendAndFinishAsync(Guid conversationId, string text, Guid attachmentId)
    {
        var send = _owner.Client.PostAsync(
            $"{Conversations}/{conversationId}/messages",
            ApiRequests.Json(new { text, selection = new { connectionId = _engineId, model = "default" }, attachmentIds = new[] { attachmentId } }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            if ((await CheckInAsync(capacity: 1))?["job"] is not JsonObject claimed)
            {
                continue;
            }

            var jobId = claimed["jobId"]!.GetValue<Guid>();
            using var finished = await _agent.PostAsync($"/v1/agent/jobs/{jobId}/finish", ApiRequests.Json(new { status = "complete" }));
            Assert.That(finished.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            using var response = await send;
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await response.Content.ReadAsStringAsync();
            return claimed["prompt"]!.GetValue<string>();
        }

        throw new TimeoutException("等不到本機代理的工作。");
    }

    private async Task<JsonNode?> CheckInAsync(int capacity)
    {
        using var response = await _agent.PostAsync(
            "/v1/agent/check-in",
            ApiRequests.Json(new
            {
                machineKey = "machine-org-attach",
                machineName = "測試電腦",
                agentVersion = AiRules.MinAgentVersionForOrganizationChat,
                engines = new[] { new { kind = "claude_code", version = "2.1.330", models = new[] { new { id = "default", displayName = "預設", efforts = Array.Empty<string>() } } } },
                capacity,
            }));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
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
}
