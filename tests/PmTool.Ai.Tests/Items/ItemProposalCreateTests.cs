using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Items;

/// <summary>建立修改建議的檢查：什麼樣的內容會被擋下、擋下時什麼都不存。</summary>
[TestFixture]
public sealed class ItemProposalCreateTests : ItemProposalTestBase
{
    [Test]
    public async Task Create_ValidChangesAndComment_StoresAPendingProposalWithTheCurrentTitle()
    {
        // Act
        var created = await CreateAsync(
            [Change("title", "新的標題"), Change("status", "done"), Change("priority", "high"), Change("dueDate", "2026-12-01"),
             Change("estimate", 3.5), Change("assignee", ItemSamples.MemberId), Change("description", "新說明")],
            "  順便留言  ");

        // Assert
        var proposal = created.Value;
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
            Assert.That(proposal.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(proposal.ItemKey, Is.EqualTo("WEB-12"));
            Assert.That(proposal.ItemTitle, Is.EqualTo("改版首頁"));
            Assert.That(proposal.Changes.Select(c => c.Field), Is.EqualTo(new[] { "title", "status", "priority", "dueDate", "estimate", "assignee", "description" }));
            Assert.That(proposal.Comment, Is.EqualTo("順便留言"));
            Assert.That(read.Value.Changes, Has.Count.EqualTo(7));
        }
    }

    [Test]
    public async Task Create_OnlyAComment_IsAllowed()
    {
        // Act
        var created = await CreateAsync([], "只有留言");

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
    }

    [Test]
    public async Task Create_ItemNotVisible_IsItemNotFound()
    {
        // Act
        var created = await CreateAsync([Change("title", "x")], itemKey: "WEB-99");

        // Assert
        AssertFailure(created, FailureKind.NotFound, AiErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task Create_ItemInAnotherProject_IsItemNotFound()
    {
        // Arrange
        H.WorkItems.Items["OPS-1"] = ItemSamples.Item("OPS-1", project: "OPS");

        // Act
        var created = await CreateAsync([Change("title", "x")], itemKey: "OPS-1");

        // Assert
        AssertFailure(created, FailureKind.NotFound, AiErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task Create_NothingToChange_IsInvalid()
    {
        // Act
        var created = await CreateAsync([], "   ");

        // Assert
        AssertFailure(created, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
    }

    [Test]
    public async Task Create_UnknownStatus_ExplainsWhichStatesExist()
    {
        // Act
        var created = await CreateAsync([Change("status", "nope")]);

        // Assert
        AssertFailure(created, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
        Assert.That(created.Failure!.Details.Single().Message, Does.Contain("todo").And.Contain("done"));
    }

    [Test]
    public async Task Create_UnknownField_IsInvalid()
    {
        // Act
        var created = await CreateAsync([Change("color", "red")]);

        // Assert
        AssertFailure(created, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
    }

    [Test]
    public async Task Create_CustomFieldNotInTheBlueprint_IsInvalid()
    {
        // Act
        var created = await CreateAsync([Change("fields.category", "a")]);

        // Assert
        AssertFailure(created, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
    }

    [Test]
    public async Task Create_DuplicateField_IsInvalid()
    {
        // Act
        var created = await CreateAsync([Change("title", "a"), Change("title", "b")]);

        // Assert
        AssertFailure(created, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
        Assert.That(created.Failure!.Details.Single().Message, Does.Contain("重複"));
    }

    [Test]
    public async Task Create_TooManyChanges_IsInvalid()
    {
        // Arrange
        var changes = Enumerable.Range(0, AiRules.MaxItemProposalChanges + 1).Select(i => Change($"fields.f{i}", "x")).ToList();

        // Act
        var created = await CreateAsync(changes);

        // Assert
        AssertFailure(created, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
    }

    [TestCase("title", 5)]
    [TestCase("title", "")]
    [TestCase("assignee", "not-a-guid")]
    [TestCase("assignee", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
    [TestCase("priority", "critical")]
    [TestCase("startDate", "12/01/2026")]
    [TestCase("dueDate", 20261201)]
    [TestCase("estimate", -1)]
    [TestCase("estimate", "3")]
    [TestCase("description", 5)]
    public async Task Create_BadValue_IsInvalid(string field, object value)
    {
        // Act
        var created = await CreateAsync([Change(field, value)]);

        // Assert
        AssertFailure(created, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
    }

    [Test]
    public async Task Create_NullClearsOptionalFields()
    {
        // Act
        var created = await CreateAsync([Change("assignee", null), Change("dueDate", null), Change("estimate", null), Change("description", null)]);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
    }

    [TestCase("title", "a\0b")]
    [TestCase("title", "兩\n行")]
    [TestCase("description", "a\u0001b")]
    public async Task Create_ControlCharacters_AreRejected(string field, string value)
    {
        // Act
        var created = await CreateAsync([Change(field, value)]);

        // Assert
        AssertFailure(created, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
    }

    [Test]
    public async Task Create_CommentWithControlCharacterOrTooLong_IsInvalid()
    {
        // Act
        var control = await CreateAsync([], "壞\0掉");
        var tooLong = await CreateAsync([], new string('字', WorkRules.MaxCommentLength + 1));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(control, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
            AssertFailure(tooLong, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
        }
    }

    [Test]
    public async Task Create_DescriptionChange_StoresTheRevisionTheModelReadNotTheCurrentOne()
    {
        // Act
        var created = await CreateAsync([Change("description", "新說明")], revision: "revision-the-model-read");

        // Assert
        await using var db = await H.Host.CreateDbContextAsync();
        var row = db.ItemProposals.Single(p => p.Id == created.Value.Id);
        Assert.That(row.DescriptionRevision, Is.EqualTo("revision-the-model-read"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Create_DescriptionChangeWithoutARevision_AsksTheModelToReadAgain(string? revision)
    {
        // Act
        var created = await CreateAsync([Change("description", "新說明")], revision: revision);

        // Assert
        AssertFailure(created, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
        Assert.That(created.Failure!.Details.Single().Message, Does.Contain("descriptionRevision").And.Contain("重讀"));
    }

    [Test]
    public async Task Create_OtherFieldsDoNotNeedARevision()
    {
        // Act
        var created = await CreateAsync([Change("title", "新標題")], revision: null);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
    }

    [Test]
    public async Task Create_ItemWithAVeryLongDescription_CannotHaveItsDescriptionChangedButCanBeCommented()
    {
        // Arrange
        H.WorkItems.Items["WEB-12"] = ItemSamples.Item(description: new string('長', 20_001));

        // Act
        var description = await CreateAsync([Change("description", "新說明")]);
        var comment = await CreateAsync([], "改用留言");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(description, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
            Assert.That(description.Failure!.Details.Single().Message, Does.Contain("留言"));
            Assert.That(comment.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Create_ReadOnlyPerson_IsForbiddenWithAToolFriendlyMessage()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember([WorkPermissions.ItemRead, WorkPermissions.BlueprintRead]);

        // Act
        var title = await CreateAsync([Change("title", "x")]);
        var status = await CreateAsync([Change("status", "done")]);
        var comment = await CreateAsync([], "留言");

        // Assert
        foreach (var result in new[] { title, status, comment })
        {
            AssertFailure(result, FailureKind.Forbidden, AiErrorCodes.Forbidden);
            Assert.That(result.Failure!.Message, Does.Contain("只能讀"));
        }
    }

    [Test]
    public async Task Create_PersonWhoMayOnlyTransition_CanProposeAStatusButNotATitle()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember([WorkPermissions.ItemRead, WorkPermissions.BlueprintRead, WorkPermissions.ItemTransition]);

        // Act
        var status = await CreateAsync([Change("status", "done")]);
        var title = await CreateAsync([Change("title", "x")]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.Succeeded, Is.True);
            AssertFailure(title, FailureKind.Forbidden, AiErrorCodes.Forbidden);
        }
    }

    [Test]
    public async Task Create_CustomFieldValueTooBig_IsInvalid()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember() with { Blueprint = Blueprint(PmTool.Blueprint.Model.FieldType.LongText) };

        // Act
        var longString = await CreateAsync([Change("fields.category", new string('字', 50_001))]);
        var fine = await CreateAsync([Change("fields.category", new string('字', 50_000))]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(longString, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
            Assert.That(fine.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Create_CustomMultiSelectWithTooManyEntries_IsInvalid()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember() with { Blueprint = Blueprint(PmTool.Blueprint.Model.FieldType.MultiSelect) };

        // Act
        var tooMany = await CreateAsync([Change("fields.category", Enumerable.Repeat("a", 201).ToArray())]);
        var fine = await CreateAsync([Change("fields.category", Enumerable.Repeat("a", 200).ToArray())]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(tooMany, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
            Assert.That(fine.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Get_ItemNoLongerVisible_IsNotFound()
    {
        // Arrange
        var created = await CreatedAsync([Change("title", "新標題")]);
        H.WorkItems.Items.Remove("WEB-12");

        // Act
        var read = await Proposals.GetAsync(Me, created.Id, CancellationToken.None);

        // Assert
        AssertFailure(read, FailureKind.NotFound, AiErrorCodes.ItemProposalNotFound);
    }

    private static PmTool.Blueprint.Model.ProjectBlueprint Blueprint(PmTool.Blueprint.Model.FieldType type) => WorkSamples.Blueprint with
    {
        Fields = [new PmTool.Blueprint.Model.FieldDefinition { Key = "category", Name = "分類", Type = type }],
        ItemTypes = [new PmTool.Blueprint.Model.ItemType { Key = "task", Name = "任務", Workflow = "flow", Fields = ["category"] }],
    };

    [Test]
    public async Task Create_Invalid_StoresNothing()
    {
        // Act
        await CreateAsync([Change("status", "nope")]);

        // Assert
        await using var db = await H.Host.CreateDbContextAsync();
        Assert.That(db.ItemProposals.Count(), Is.Zero);
    }

    private static void AssertFailure(Result<ItemProposal> result, FailureKind kind, string code)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure?.Kind, Is.EqualTo(kind));
            Assert.That(result.Failure?.Code, Is.EqualTo(code));
        }
    }
}
