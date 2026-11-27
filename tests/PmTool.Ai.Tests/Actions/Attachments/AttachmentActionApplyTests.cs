using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Actions;
using PmTool.Ai.Tests.Assistant.Fakes;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Actions.Attachments;

/// <summary>附件建議套用：用使用者的身分把檔案交給項目或知識庫的附件服務，每一步都再讀一次檔案、再確認一次。</summary>
[TestFixture]
public sealed class AttachmentActionApplyTests : AttachmentActionTestBase
{
    private Task<Result<ActionProposal>> CreateAsync(string changesJson, string? projectKey = "WEB") =>
        Actions.Creator.CreateAsync(
            Me, new ActionProposalDraft(ActionKinds.Attachments, projectKey, Payload(changesJson)) { ConversationId = ConversationId }, CancellationToken.None);

    private Task<Result<ActionProposal>> ApplyAsync(Guid proposalId) =>
        Actions.Service.ApplyAsync(Me, proposalId, new Dictionary<string, string>(), CancellationToken.None);

    [Test]
    public async Task Apply_AttachToItem_GivesTheOriginalFileNameAndBytesToTheItemAttachmentService()
    {
        // Arrange
        var proposal = (await CreateAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"}]""")).Value;

        // Act
        var applied = await ApplyAsync(proposal.Id);

        // Assert
        var added = ItemFiles.Added.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(applied.Value.Steps.Single().Done, Is.True);
            Assert.That(applied.Value.Steps.Single().Link, Is.EqualTo("/projects/WEB/items/WEB-12"));
            Assert.That(added.Target, Is.EqualTo("WEB-12"));
            Assert.That(added.FileName, Is.EqualTo("規格.pdf"));
            Assert.That(added.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(added.Content, Is.EqualTo(PdfBytes));
        }
    }

    [Test]
    public async Task Apply_AttachToPage_GivesTheFileToTheKnowledgeAttachmentService()
    {
        // Arrange
        Pages.Add("HR", 12, "新人須知");
        var proposal = (await CreateAsync("""[{"action":"attach","attachmentId":"規格.pdf","spaceKey":"HR","pageNumber":12}]""", projectKey: null)).Value;

        // Act
        var applied = await ApplyAsync(proposal.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Steps.Single().Link, Is.EqualTo("/knowledge/HR/12"));
            Assert.That(PageFiles.Added.Single().Target, Is.EqualTo("HR/12"));
            Assert.That(PageFiles.Added.Single().Content, Is.EqualTo(PdfBytes));
            Assert.That(ItemFiles.Added, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_TheFileIsGoneFromStorage_StepFailsWithNotFoundInsteadOfThrowing()
    {
        // Arrange
        var proposal = (await CreateAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"}]""")).Value;
        await H.Store().DeleteAsync(PmTool.Ai.Attachments.AttachmentMapper.KeyOf(Spec.Id), CancellationToken.None);

        // Act
        var applied = await ApplyAsync(proposal.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure!.Code, Is.EqualTo(AiErrorCodes.AttachmentNotFound));
            Assert.That(applied.Failure.Message, Is.EqualTo("找不到這個檔案。"));
            Assert.That(ItemFiles.Added, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_AttachmentServiceRejectsLaterStep_ThatStepShowsTheErrorAndEarlierOnesStayDone()
    {
        // Arrange
        H.WorkItems.Items["WEB-13"] = ItemSamples.Item("WEB-13", "另一個");
        var proposal = (await CreateAsync(
            """[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"},{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-13"}]""")).Value;
        var handler = Handler;
        var prepared = (await handler.PrepareAsync(
            new ActionContext(Me, "WEB", ConversationId),
            Payload("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"},{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-13"}]"""),
            CancellationToken.None)).Value;
        var context = new ActionContext(Me, "WEB", ConversationId);

        // Act
        var first = await handler.ApplyStepAsync(context, prepared.State, 0, new Dictionary<string, string>(), CancellationToken.None);
        ItemFiles.RejectNext = new Failure(FailureKind.Conflict, "work.attachment_limit_reached", "附件太多了。");
        var second = await handler.ApplyStepAsync(context, prepared.State, 1, new Dictionary<string, string>(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposal.Steps, Has.Count.EqualTo(2));
            Assert.That(first.Failure, Is.Null);
            Assert.That(second.Failure!.Message, Is.EqualTo("附件太多了。"));
            Assert.That(ItemFiles.Added, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_DeleteAttachment_CallsDeleteWithTheAttachmentId()
    {
        // Arrange
        var old = ItemFiles.Seed("WEB-12", "舊圖.png");
        var proposal = (await CreateAsync($$"""[{"action":"delete","attachmentId":"{{old.Id}}","itemKey":"WEB-12"}]""")).Value;

        // Act
        var applied = await ApplyAsync(proposal.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Steps.Single().Done, Is.True);
            Assert.That(ItemFiles.Deleted, Is.EqualTo(new[] { old.Id }));
        }
    }

    [Test]
    public async Task Apply_DeleteAttachmentThatIsAlreadyGone_FailsWithoutCallingDelete()
    {
        // Arrange
        var old = ItemFiles.Seed("WEB-12", "舊圖.png");
        var proposal = (await CreateAsync($$"""[{"action":"delete","attachmentId":"{{old.Id}}","itemKey":"WEB-12"}]""")).Value;
        ItemFiles.ByItem["WEB-12"].Clear();

        // Act
        var applied = await ApplyAsync(proposal.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure!.Code, Is.EqualTo(AiErrorCodes.AttachmentNotFound));
            Assert.That(ItemFiles.Deleted, Is.Empty);
        }
    }

    [Test]
    public async Task Create_StoresNoFileContentAndTheConversationIsKept()
    {
        // Arrange
        var proposal = (await CreateAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"}]""")).Value;

        // Act
        await using var db = await H.Host.CreateDbContextAsync();
        var row = await db.ActionProposals.AsNoTracking().SingleAsync(p => p.Id == proposal.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.ConversationId, Is.EqualTo(ConversationId));
            Assert.That(row.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(row.Kind, Is.EqualTo(ActionKinds.Attachments));
            Assert.That(row.State.Length, Is.LessThan(1000), "狀態只放編號與檔名，不放檔案的內容");
        }
    }
}
