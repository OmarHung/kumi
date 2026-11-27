using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Ai.Assistant.Tools;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.Actions.Attachments;

/// <summary>內建助理的 propose_attachment_changes、propose_people_changes：參數、建議屬於哪個專案、結果的 proposalId、Schema 與說明。</summary>
[TestFixture]
public sealed class PeopleAttachmentToolTests : AttachmentActionTestBase
{
    private RecordingReads _reads = new();

    // 同一個測試類別的實例會重複使用，記錄要每個測試重來。
    [SetUp]
    public void ResetReads() => _reads = new RecordingReads();

    private sealed class RecordingReads : IAssistantConversationReads
    {
        public List<string> Remembered { get; } = [];

        public Task RememberAsync(PmTool.Abstractions.Actor actor, Guid? conversationId, IReadOnlyCollection<string> projectKeys, CancellationToken cancellationToken)
        {
            Remembered.AddRange(projectKeys);
            return Task.CompletedTask;
        }
    }

    private AssistantToolContext ToolContext(string? projectKey = null, PmTool.Ai.Actions.IActionProposalHandler? handler = null)
    {
        var creator = handler is null ? Actions.Creator : new PmTool.Ai.Tests.Actions.People.IsolatedActions(H, handler).Creator;
        var services = new ServiceCollection().AddSingleton(creator).AddSingleton<IAssistantConversationReads>(_reads).BuildServiceProvider();
        return H.NewToolContext(projectKey, ConversationId) with { Services = services };
    }

    private static async Task<JsonElement> RunAsync(AIFunction tool, string argumentsJson)
    {
        var arguments = new AIFunctionArguments(JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson));
        using var document = JsonDocument.Parse((string)(await tool.InvokeAsync(arguments))!);
        return document.RootElement.Clone();
    }

    [Test]
    public async Task ProposeAttachmentChanges_InAnOrganizationConversation_TakesTheProjectFromTheItemKeyAndReturnsProposalId()
    {
        // Arrange
        var tool = new ProposeAttachmentChangesTool(ToolContext(projectKey: null));

        // Act
        var result = await RunAsync(tool, """{"changes":[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"}],"reason":"附上規格"}""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True, result.GetRawText());
            Assert.That(Guid.TryParse(result.GetProperty("proposalId").GetString(), out _), Is.True);
            Assert.That(result.GetProperty("projectKey").GetString(), Is.EqualTo("WEB"));
            Assert.That(result.GetProperty("note").GetString(), Does.Contain("套用"));
        }
    }

    [Test]
    public async Task ProposeAttachmentChanges_ItemsFromTwoProjects_IsRejectedBeforeCreating()
    {
        // Arrange
        var tool = new ProposeAttachmentChangesTool(ToolContext());

        // Act
        var result = await RunAsync(tool, """{"changes":[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"},{"action":"attach","attachmentId":"規格.pdf","itemKey":"OPS-1"}]}""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("同一個專案"));
        }
    }

    [Test]
    public async Task ProposeAttachmentChanges_InAProjectConversation_RefusesItemsOfAnotherProjectWithoutTouchingThem()
    {
        // Arrange
        var tool = new ProposeAttachmentChangesTool(ToolContext(projectKey: "WEB"));

        // Act
        var result = await RunAsync(tool, """{"changes":[{"action":"delete","attachmentId":"x","itemKey":"OPS-1"}]}""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Is.EqualTo("這則對話只能處理專案 WEB 的項目。"));
            Assert.That(_reads.Remembered, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeAttachmentChanges_InAnOrganizationConversation_RemembersTheProjectItRead()
    {
        // Arrange
        var tool = new ProposeAttachmentChangesTool(ToolContext(projectKey: null));

        // Act
        await RunAsync(tool, """{"changes":[{"action":"delete","attachmentId":"x","itemKey":"WEB-12"}]}""");

        // Assert
        Assert.That(_reads.Remembered, Is.EqualTo(new[] { "WEB" }));
    }

    [Test]
    public async Task ProposeAttachmentChanges_PageOnly_HasNoProject()
    {
        // Arrange
        Pages.Add("HR", 12, "新人須知");
        var tool = new ProposeAttachmentChangesTool(ToolContext());

        // Act
        var result = await RunAsync(tool, """{"changes":[{"action":"attach","attachmentId":"規格.pdf","spaceKey":"HR","pageNumber":12}]}""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True, result.GetRawText());
            Assert.That(result.TryGetProperty("projectKey", out var key) ? key.ValueKind : JsonValueKind.Null, Is.EqualTo(JsonValueKind.Null));
        }
    }

    [Test]
    public async Task Tools_UnknownTopLevelParameterOrBadChanges_AreReportedToTheModel()
    {
        // Arrange
        var attach = new ProposeAttachmentChangesTool(ToolContext());
        var people = new ProposePeopleChangesTool(ToolContext());

        // Act
        var extra = await RunAsync(attach, """{"changes":[],"password":"x"}""");
        var notArray = await RunAsync(people, """{"changes":"nope"}""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(extra.GetProperty("error").GetString(), Does.Contain("password"));
            Assert.That(notArray.GetProperty("ok").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task ProposePeopleChanges_ByNonAdmin_ReturnsTheForbiddenMessage()
    {
        // Arrange
        var tool = new ProposePeopleChangesTool(
            ToolContext(handler: new PmTool.Ai.Actions.People.PeopleActionHandler(new PmTool.Ai.Tests.Actions.People.RecordingPeople(), H.Host.Services)));

        // Act
        var result = await RunAsync(tool, $$"""{"changes":[{"action":"disable","person":"{{Guid.NewGuid()}}"}]}""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Is.EqualTo("人員管理只有組織管理員能做。"));
        }
    }

    [Test]
    public void Schemas_OnlyUseAllowedKeywordsAndPeopleHasNoPasswordProperty()
    {
        // Arrange
        var allowed = new HashSet<string> { "type", "properties", "required", "additionalProperties", "items", "enum", "const", "$ref", "description" };
        var tools = new AIFunction[] { new ProposeAttachmentChangesTool(ToolContext()), new ProposePeopleChangesTool(ToolContext()) };

        // Act
        var keywords = tools.SelectMany(t => Keywords(t.JsonSchema)).Distinct().ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(keywords.Where(k => !allowed.Contains(k)), Is.Empty);
            Assert.That(tools[1].JsonSchema.GetRawText(), Does.Not.Contain("\"password\""));
            Assert.That(tools[1].Description, Does.Contain("create {displayName, email, role, jobTitle?, responsibilities?}"));
            Assert.That(tools[1].JsonSchema.GetRawText(), Does.Contain("create、set_profile"));
            Assert.That(tools[1].Description, Does.Contain("絕對不要問使用者").And.Contain("請按套用"));
            Assert.That(tools[0].Description, Does.Contain("請按套用").And.Contain("不能復原"));
        }
    }

    // 屬性名稱底下的子物件（properties 的每一個鍵）不是關鍵字，只看 Schema 自己的關鍵字。
    private static IEnumerable<string> Keywords(JsonElement schema)
    {
        foreach (var property in schema.EnumerateObject())
        {
            yield return property.Name;
            if (property.Name == "properties")
            {
                foreach (var child in property.Value.EnumerateObject().SelectMany(c => Keywords(c.Value)))
                {
                    yield return child;
                }
            }
            else if (property.Name == "items" && property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var child in Keywords(property.Value))
                {
                    yield return child;
                }
            }
        }
    }
}
