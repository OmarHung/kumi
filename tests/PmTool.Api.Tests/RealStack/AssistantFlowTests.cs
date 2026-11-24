using System.Net;
using Npgsql;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 專案的 AI 助理的對話從 HTTP 一路到資料庫：真的容器接線（AI 模組用到工作引擎的服務）、真的權限、真的資料表。
/// 不呼叫模型（連線與供應商要有真的金鑰）；呼叫模型的回覆迴圈在 AI 模組的測試裡用腳本模型驗證。
/// </summary>
[TestFixture]
public sealed class AssistantFlowTests : RealStackTestBase
{
    private const string Project = "ASST";

    private static readonly string Collection = $"/v1/projects/{Project}/assistant/conversations";

    private SignedInClient _owner = null!;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(_owner.Client, Project, "simple_tasks");
    }

    [OneTimeTearDown]
    public void TearDown() => _owner.Dispose();

    [Test]
    public async Task Conversations_FullFlow_RequireAiToBeOn_ThenCreateListReadAndDelete()
    {
        // Act：先關著 AI（同一個資料庫裡別的測試可能已經開了）。
        await SetAiAsync(enabled: false);
        using var beforeEnabled = await _owner.Client.PostAsync(Collection, null);
        await SetAiAsync(enabled: true);
        using var created = await _owner.Client.PostAsync(Collection, null);
        var conversation = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var route = $"{Collection}/{conversation?["id"]?.GetValue<Guid>()}";
        var listed = await RealStackSteps.GetAsync(_owner.Client, Collection);
        var read = await RealStackSteps.GetAsync(_owner.Client, route);
        using var deleted = await _owner.Client.DeleteAsync(route);
        using var gone = await _owner.Client.GetAsync(route);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(beforeEnabled, HttpStatusCode.UnprocessableEntity, AiErrorCodes.Unavailable);
            Assert.That(created.Headers.Location?.ToString(), Is.EqualTo(route));
            Assert.That(conversation?["messageCount"]?.GetValue<int>(), Is.Zero);
            Assert.That(conversation?["isRunning"]?.GetValue<bool>(), Is.False);
            Assert.That(listed.AsArray().Select(c => c?["id"]?.GetValue<Guid>()), Is.EqualTo(new[] { conversation?["id"]?.GetValue<Guid>() }));
            Assert.That(read["messages"]?.AsArray(), Is.Empty);
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            await ApiRequests.AssertErrorEnvelopeAsync(gone, HttpStatusCode.NotFound, AiErrorCodes.ConversationNotFound);
        }
    }

    [Test]
    public async Task Conversations_AreOnlyTheirOwners_EvenForTheOrganizationOwner()
    {
        // Arrange
        await SetAiAsync(enabled: true);
        using var editor = await AddMemberAsync(_owner, Project, "editor", "Editor Example", RealStackSteps.UniqueEmail("assistant-editor"));
        using var created = await editor.Client.PostAsync(Collection, null);
        var conversation = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var route = $"{Collection}/{conversation?["id"]?.GetValue<Guid>()}";

        // Act
        var ownersList = await RealStackSteps.GetAsync(_owner.Client, Collection);
        using var ownersRead = await _owner.Client.GetAsync(route);
        using var ownersDelete = await _owner.Client.DeleteAsync(route);
        var editorsList = await RealStackSteps.GetAsync(editor.Client, Collection);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ownersList.AsArray().Select(c => c?["id"]?.GetValue<Guid>()), Does.Not.Contain(conversation?["id"]?.GetValue<Guid>()));
            Assert.That(editorsList.AsArray().Select(c => c?["id"]?.GetValue<Guid>()), Does.Contain(conversation?["id"]?.GetValue<Guid>()));
            await ApiRequests.AssertErrorEnvelopeAsync(ownersRead, HttpStatusCode.NotFound, AiErrorCodes.ConversationNotFound);
            await ApiRequests.AssertErrorEnvelopeAsync(ownersDelete, HttpStatusCode.NotFound, AiErrorCodes.ConversationNotFound);
        }
    }

    [Test]
    public async Task Conversations_PersonWhoIsNotAMember_SeesTheProjectAsNotFound()
    {
        // Arrange：組織的一般成員，沒有加進這個專案。
        await SetAiAsync(enabled: true);
        var person = await RealStackSteps.CreateMemberAsync(_owner.Client, "Outsider Example", RealStackSteps.UniqueEmail("assistant-outsider"));
        using var outsider = await SignInAsAsync(person["email"]!.GetValue<string>(), RealStackSteps.MemberPassword);

        // Act
        using var list = await outsider.Client.GetAsync(Collection);
        using var create = await outsider.Client.PostAsync(Collection, null);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(list, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(create, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    [Test]
    public async Task Send_ToAConnectionThatDoesNotExist_Is422SelectionInvalid_AndNothingIsStored()
    {
        // Arrange
        await SetAiAsync(enabled: true);
        using var created = await _owner.Client.PostAsync(Collection, null);
        var conversation = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var route = $"{Collection}/{conversation?["id"]?.GetValue<Guid>()}";

        // Act
        using var response = await _owner.Client.PostAsync(
            $"{route}/messages",
            ApiRequests.Json(new { text = "把驗收拆成兩段", selection = new { connectionId = Guid.NewGuid(), model = "claude-opus-5-5" } }));
        var read = await RealStackSteps.GetAsync(_owner.Client, route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.SelectionInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read["messages"]?.AsArray(), Is.Empty);
            Assert.That(read["conversation"]?["messageCount"]?.GetValue<int>(), Is.Zero);
        }
    }

    // 不透過設定的服務：它要連供應商讀模型清單。這裡只需要「組織開著（或關著）AI」這一列。
    private async Task SetAiAsync(bool enabled)
    {
        await using var connection = new NpgsqlConnection(Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO ai.settings (organization_id, enabled, updated_at, updated_by)
            SELECT id, @enabled, now(), @person FROM identity.organizations
            ON CONFLICT (organization_id) DO UPDATE SET enabled = @enabled
            """,
            connection);
        command.Parameters.AddWithValue("person", _owner.PersonId);
        command.Parameters.AddWithValue("enabled", enabled);
        await command.ExecuteNonQueryAsync();
    }
}
