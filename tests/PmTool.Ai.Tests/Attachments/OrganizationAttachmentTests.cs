using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>
/// 不選專案的組織對話的附件：上傳、刪除、下載都不碰任何專案的權限；組織的附件和專案的附件分開，
/// 用錯一邊（組織的拿去專案的對話送、用專案的代號下載組織的）一律當作不存在。
/// </summary>
[TestFixture]
public sealed class OrganizationAttachmentTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Upload_WithoutAProject_IsPendingAndNeverAsksTheProjectService()
    {
        // Act
        var result = await TryUploadToOrganizationAsync("需求.pdf", Pdf());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
            Assert.That(result.Value.Kind, Is.EqualTo(AssistantAttachmentKind.Pdf));
            Assert.That(_h.Store().Contains(result.Value.Id), Is.True);
            Assert.That(_h.Projects.Gets, Is.Empty);
        }
    }

    [Test]
    public async Task Upload_WithoutAProjectWhenAiIsTurnedOff_IsUnavailable()
    {
        // Arrange
        await _h.Host.Get<IAiSettingsService>().SaveAsync(_h.Admin, new AiSettingsInput(false, null), CancellationToken.None);

        // Act
        var result = await TryUploadToOrganizationAsync("a.png", Image());

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.Unavailable);
    }

    [Test]
    public async Task Send_InAnOrganizationConversation_BindsTheAttachmentsAndTheModelReadsThem()
    {
        // Arrange
        var attachment = await UploadToOrganizationAsync("需求.md", Text("# 需求\n做一個報名網站"));
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Reply("看到了");

        // Act
        var started = await StartInOrganizationAsync(conversation.Id, "照這份開專案", attachment.Id);
        await AssistantHarness.ReadAllAsync(started.Value);
        var messages = await _h.OrganizationMessagesAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
            Assert.That(messages[0].Attachments.Select(a => a.Id), Is.EqualTo(new[] { attachment.Id }));
            var sent = _h.Script.Calls.Single().Messages.Last();
            Assert.That(
                sent.Contents.OfType<TextContent>().Select(t => t.Text),
                Has.Member(PmTool.Ai.Attachments.AttachmentReplay.FormatText("需求.md", "# 需求\n做一個報名網站")));
        }
    }

    [Test]
    public async Task Send_AProjectAttachmentInAnOrganizationConversation_IsNotPendingAndStoresNothing()
    {
        // Arrange
        var fromProject = await _h.UploadAsync("a.png", Image());
        var conversation = await _h.NewOrganizationConversationAsync();

        // Act
        var started = await StartInOrganizationAsync(conversation.Id, "看這個", fromProject.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(started.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentNotFound);
            Assert.That((await _h.ConversationRowAsync(conversation.Id)).MessageCount, Is.Zero);
        }
    }

    [Test]
    public async Task Send_AnOrganizationAttachmentInAProjectConversation_IsNotPending()
    {
        // Arrange
        var fromOrganization = await UploadToOrganizationAsync("a.png", Image());
        var conversation = await _h.NewConversationAsync();

        // Act
        var started = await _h.TryStartAsync(conversation.Id, "看這個", [fromOrganization.Id]);

        // Assert
        AssertFailure(started.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Send_AnExpiredOrganizationAttachment_IsNotFoundAndStoresNothing()
    {
        // Arrange
        var attachment = await UploadToOrganizationAsync("a.png", Image());
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Clock.Advance(AssistantAttachmentRules.PendingLifetime + TimeSpan.FromMinutes(1));

        // Act
        var started = await StartInOrganizationAsync(conversation.Id, "看這個", attachment.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(started.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentNotFound);
            Assert.That((await _h.ConversationRowAsync(conversation.Id)).MessageCount, Is.Zero);
        }
    }

    [Test]
    public async Task Open_OrganizationAttachment_OnlyItsOwnerReadsItAndOnlyWithoutAProject()
    {
        // Arrange
        var content = Image(300);
        var attachment = await UploadToOrganizationAsync("a.png", content);

        // Act
        await using var opened = (await _h.Attachments().OpenAsync(_h.Actor, null, attachment.Id, CancellationToken.None)).Value;
        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer);
        var byOther = await _h.Attachments().OpenAsync(_h.Admin, null, attachment.Id, CancellationToken.None);
        var viaProject = await _h.Attachments().OpenAsync(_h.Actor, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(buffer.ToArray(), Is.EqualTo(content));
            AssertFailure(byOther.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
            AssertFailure(viaProject.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    [Test]
    public async Task Open_ProjectAttachmentWithoutAProject_IsNotFound()
    {
        // Arrange
        var attachment = await _h.UploadAsync("a.png", Image());

        // Act
        var result = await _h.Attachments().OpenAsync(_h.Actor, null, attachment.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Open_SentInAnOrganizationConversation_FollowsTheConversation()
    {
        // Arrange
        var attachment = await UploadToOrganizationAsync("a.png", Image());
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Reply("好");
        await AssistantHarness.ReadAllAsync((await StartInOrganizationAsync(conversation.Id, "看這個", attachment.Id)).Value);
        var beforeDelete = await _h.Attachments().OpenAsync(_h.Actor, null, attachment.Id, CancellationToken.None);
        await using (beforeDelete.Value)
        {
        }

        // Act
        await _h.Service.DeleteAsync(_h.Actor, null, conversation.Id, CancellationToken.None);
        var afterDelete = await _h.Attachments().OpenAsync(_h.Actor, null, attachment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(beforeDelete.Succeeded, Is.True);
            AssertFailure(afterDelete.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    [Test]
    public async Task DeletePending_OrganizationAttachment_RemovesItOnlyWithoutAProject()
    {
        // Arrange
        var attachment = await UploadToOrganizationAsync("a.png", Image());

        // Act
        var viaProject = await _h.Attachments().DeletePendingAsync(_h.Actor, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);
        var stillThere = _h.Store().Contains(attachment.Id);
        var deleted = await _h.Attachments().DeletePendingAsync(_h.Actor, null, attachment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(viaProject.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
            Assert.That(stillThere, Is.True);
            Assert.That(deleted.Succeeded, Is.True);
            Assert.That(_h.Store().Contains(attachment.Id), Is.False);
        }
    }

    private async Task<Result<AssistantAttachment>> TryUploadToOrganizationAsync(string fileName, byte[] content)
    {
        await using var stream = new MemoryStream(content);
        return await _h.Attachments().UploadAsync(
            _h.Actor, null, new NewAssistantAttachment { FileName = fileName, Content = stream }, CancellationToken.None);
    }

    private async Task<AssistantAttachment> UploadToOrganizationAsync(string fileName, byte[] content)
    {
        var result = await TryUploadToOrganizationAsync(fileName, content);
        Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
        return result.Value;
    }

    private Task<Result<AssistantRun>> StartInOrganizationAsync(Guid conversationId, string text, params Guid[] attachmentIds) =>
        _h.Service.StartAsync(
            _h.Actor, null, conversationId, new AssistantMessageInput(text, _h.Selection) { AttachmentIds = attachmentIds }, CancellationToken.None);
}
