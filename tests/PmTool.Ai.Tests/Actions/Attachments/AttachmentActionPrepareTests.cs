using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Actions.Attachments;

/// <summary>附件建議建立時的檢查：只認這則對話裡自己上傳的檔案、項目或頁面看得到也寫得了才收、每個問題一次回完。</summary>
[TestFixture]
public sealed class AttachmentActionPrepareTests : AttachmentActionTestBase
{
    [Test]
    public async Task Prepare_AttachByFileNameToAnItem_DescribesTheStepAndKeepsOnlyIdsInState()
    {
        // Act
        var result = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"}]""");

        // Assert
        var prepared = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Steps[0].Label, Is.EqualTo("附加檔案"));
            Assert.That(prepared.Steps[0].Target, Is.EqualTo("WEB-12 改版首頁"));
            Assert.That(prepared.Steps[0].After, Does.StartWith("規格.pdf（"));
            Assert.That(prepared.Steps[0].Before, Is.Null);
            Assert.That(prepared.Inputs, Is.Empty);
            Assert.That(prepared.State.GetRawText(), Does.Contain(Spec.Id.ToString()).And.Contain("WEB-12"));
        }
    }

    [Test]
    public async Task Prepare_AttachByIdIsCaseInsensitiveOnNamesAndAcceptsTheGuid()
    {
        // Act
        var byId = await PrepareAsync($$"""[{"action":"attach","attachmentId":"{{Spec.Id}}","itemKey":"WEB-12"}]""");
        var byUpperName = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.PDF","itemKey":"WEB-12"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byId.Succeeded, Is.True);
            Assert.That(byUpperName.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Prepare_FileFromAnotherConversationOrAnotherPersonOrUnsentOrNoConversation_IsAlwaysNotFound()
    {
        // Arrange
        var otherConversation = await H.NewConversationAsync();
        var inOther = await SendFileAsync(otherConversation.Id, "別則.pdf", PdfBytes);
        var someoneElsesConversation = await H.NewConversationAsync(H.Admin);
        var theirs = await SendFileAsync(someoneElsesConversation.Id, "別人的.pdf", PdfBytes, H.Admin);
        var unsent = await H.UploadAsync("還沒送.pdf", PdfBytes);

        // Act
        var results = new List<Result<PmTool.Ai.Actions.PreparedAction>>();
        foreach (var id in new[] { inOther.Id, theirs.Id, unsent.Id })
        {
            results.Add(await PrepareAsync($$"""[{"action":"attach","attachmentId":"{{id}}","itemKey":"WEB-12"}]"""));
        }

        var noConversation = await Handler.PrepareAsync(
            new PmTool.Ai.Actions.ActionContext(Me, null, null), Payload($$"""[{"action":"attach","attachmentId":"{{Spec.Id}}","itemKey":"WEB-12"}]"""), CancellationToken.None);
        results.Add(noConversation);

        // Assert
        Assert.That(results.Select(r => r.Failure!.Details.Single().Message), Has.All.StartWith("找不到這個檔案"));
        Assert.That(results.Select(r => r.Failure!.Details.Single().Path), Has.All.EqualTo("changes[0].attachmentId"));
    }

    [Test]
    public async Task Prepare_NotFoundMessageListsOnlyThisConversationsFiles()
    {
        // Arrange
        var otherConversation = await H.NewConversationAsync();
        await SendFileAsync(otherConversation.Id, "別則.pdf", PdfBytes);

        // Act
        var result = await PrepareAsync("""[{"action":"attach","attachmentId":"別則.pdf","itemKey":"WEB-12"}]""");

        // Assert
        var message = result.Failure!.Details.Single().Message;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("規格.pdf"));
            Assert.That(message, Does.Not.Contain("別則.pdf（"));
        }
    }

    [Test]
    public async Task Prepare_TwoFilesWithTheSameName_AsksForTheIdAndListsBoth()
    {
        // Arrange
        var again = await SendFileAsync(ConversationId, "規格.pdf", PdfBytes);

        // Act
        var byName = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"}]""");
        var byId = await PrepareAsync($$"""[{"action":"attach","attachmentId":"{{again.Id}}","itemKey":"WEB-12"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byName.Failure!.Details.Single().Message, Does.Contain(Spec.Id.ToString()).And.Contain(again.Id.ToString()));
            Assert.That(byId.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Prepare_ItemNotVisibleOrInAnotherProject_IsReportedAsNotFound()
    {
        // Arrange
        H.WorkItems.Items["OPS-1"] = ItemSamples.Item("OPS-1", "別的專案", "OPS");

        // Act
        var missing = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-999"}]""");
        var other = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"OPS-1"}]""", projectKey: "WEB");
        var allowedWhenNoProjectScope = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"OPS-1"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.Failure!.Details.Single().Message, Does.Contain("找不到項目"));
            Assert.That(other.Failure!.Details.Single().Message, Does.Contain("找不到項目"));
            Assert.That(allowedWhenNoProjectScope.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Prepare_WithoutItemUpdatePermissionOrOnArchivedProject_IsRejected()
    {
        // Arrange
        UseProject(canUpdate: false);
        var noPermission = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"}]""");
        UseProject(canUpdate: true, archived: true);

        // Act
        var archived = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(noPermission.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(noPermission.Failure.Message, Does.Contain("item.update"));
            Assert.That(archived.Failure!.Details.Single().Message, Does.Contain("已經封存"));
        }
    }

    [Test]
    public async Task Prepare_AttachToPage_NeedsAnEditablePage()
    {
        // Arrange
        Pages.Add("HR", 12, "新人須知");
        Pages.Add("HR", 13, "唯讀頁", canEdit: false);

        // Act
        var ok = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","spaceKey":"HR","pageNumber":12}]""");
        var readOnly = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","spaceKey":"HR","pageNumber":13}]""");
        var missing = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","spaceKey":"HR","pageNumber":99}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok.Value.Steps[0].Target, Is.EqualTo("HR／新人須知"));
            Assert.That(readOnly.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(missing.Failure!.Details.Single().Message, Does.Contain("找不到知識庫頁面"));
        }
    }

    [Test]
    public async Task Prepare_TargetShapeProblems_AreAllReportedWithPaths()
    {
        // Act
        var result = await PrepareAsync("""
            [{"action":"attach","attachmentId":"規格.pdf"},
             {"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12","spaceKey":"HR","pageNumber":1},
             {"action":"attach","attachmentId":"規格.pdf","spaceKey":"HR"},
             {"action":"delete","attachmentId":"規格.pdf","spaceKey":"HR","pageNumber":1},
             {"action":"archive","attachmentId":"x"},
             {"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12","extra":1}]
            """);

        // Assert
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[]
        {
            "changes[0]", "changes[1]", "changes[2].pageNumber", "changes[3]", "changes[4].action", "changes[5].extra",
        }));
    }

    [Test]
    public async Task Prepare_SameFileToSameTargetTwice_IsRejectedButTwoTargetsAreFine()
    {
        // Arrange
        H.WorkItems.Items["WEB-13"] = ItemSamples.Item("WEB-13", "另一個");

        // Act
        var twice = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"},{"action":"attach","attachmentId":"規格.pdf","itemKey":"web-12"}]""");
        var two = await PrepareAsync("""[{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-12"},{"action":"attach","attachmentId":"規格.pdf","itemKey":"WEB-13"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(twice.Failure!.Details.Single().Path, Is.EqualTo("changes[1]"));
            Assert.That(two.Value.Steps, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Prepare_DeleteByIdOrName_WarnsItCannotBeUndone()
    {
        // Arrange
        var old = ItemFiles.Seed("WEB-12", "舊圖.png");

        // Act
        var byId = await PrepareAsync($$"""[{"action":"delete","attachmentId":"{{old.Id}}","itemKey":"WEB-12"}]""");
        var byName = await PrepareAsync("""[{"action":"delete","attachmentId":"舊圖.png","itemKey":"WEB-12"}]""");
        var unknown = await PrepareAsync("""[{"action":"delete","attachmentId":"沒有.png","itemKey":"WEB-12"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byId.Value.Steps[0].Label, Is.EqualTo("刪除附件"));
            Assert.That(byId.Value.Steps[0].Before, Is.EqualTo("舊圖.png"));
            Assert.That(byId.Value.Steps[0].After, Is.Null);
            Assert.That(byId.Value.Steps[0].Note, Does.Contain("不能復原"));
            Assert.That(byName.Succeeded, Is.True);
            Assert.That(unknown.Failure!.Details.Single().Message, Does.Contain("沒有這個附件").And.Contain("舊圖.png"));
        }
    }

    [Test]
    public async Task Prepare_DeleteWithoutItemUpdate_IsForbidden()
    {
        // Arrange
        var old = ItemFiles.Seed("WEB-12", "舊圖.png");
        UseProject(canUpdate: false);

        // Act
        var result = await PrepareAsync($$"""[{"action":"delete","attachmentId":"{{old.Id}}","itemKey":"WEB-12"}]""");

        // Assert
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
    }
}
