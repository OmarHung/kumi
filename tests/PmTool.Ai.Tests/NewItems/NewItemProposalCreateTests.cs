using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.NewItems;

/// <summary>建立新增項目的建議：什麼樣的內容會被擋下（每個問題一個細項）、擋下時什麼都不存。</summary>
[TestFixture]
public sealed class NewItemProposalCreateTests : NewItemProposalTestBase
{
    [Test]
    public async Task Create_ValidItemsWithParentRefAndFields_StoresAPendingProposal()
    {
        // Arrange
        var items = new[]
        {
            Epic(),
            Task("備份資料庫") with { Description = "  做完整備份  ", AssigneeId = ItemSamples.MemberId, Priority = "HIGH", DueDate = "2026-12-01", Estimate = 3.5m, Fields = new Dictionary<string, System.Text.Json.JsonElement> { ["category"] = Json("資料") } },
            Task("切換 DNS", parentRef: null, parentKey: "WEB-12"),
        };

        // Act
        var created = await CreateAsync(items);

        // Assert
        var read = await Proposals.GetAsync(Me, created.Value.Id, CancellationToken.None);
        var stored = read.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
            Assert.That(stored.Status, Is.EqualTo(NewItemProposalStatus.Pending));
            Assert.That(stored.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(stored.Items.Select(i => i.Title), Is.EqualTo(new[] { "搬到 AWS", "備份資料庫", "切換 DNS" }));
            Assert.That(stored.CreatedKeys, Is.EqualTo(new string?[] { null, null, null }));
            Assert.That(stored.Items[1].ParentRef, Is.EqualTo("e"));
            Assert.That(stored.Items[1].Description, Is.EqualTo("做完整備份"));
            Assert.That(stored.Items[1].Priority, Is.EqualTo("high"));
            Assert.That(stored.Items[1].Estimate, Is.EqualTo(3.5m));
            Assert.That(stored.Items[1].Fields["category"].GetString(), Is.EqualTo("資料"));
            Assert.That(stored.Items[2].ParentKey, Is.EqualTo("WEB-12"));
            Assert.That(H.WorkItems.Creates, Is.Empty, "建議不會建立項目");
        }
    }

    [Test]
    public async Task Create_WhitespaceDescription_BecomesNull()
    {
        // Act
        var created = await CreateAsync(new NewItemDraft("bug", "  頁面壞了  ") { Description = "   " });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Value.Items.Single().Title, Is.EqualTo("頁面壞了"));
            Assert.That(created.Value.Items.Single().Description, Is.Null);
        }
    }

    [Test]
    public async Task Create_WithoutItemCreatePermission_IsForbidden()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember([PmTool.Work.WorkPermissions.ItemRead]) with { Blueprint = Blueprint() };

        // Act
        var created = await CreateAsync(Epic());

        // Assert
        AssertFailure(created, FailureKind.Forbidden, AiErrorCodes.Forbidden);
    }

    [Test]
    public async Task Create_ProjectNotVisible_ReturnsTheProjectFailureAsIs()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var created = await CreateAsync(Epic());

        // Assert
        AssertFailure(created, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    [Test]
    public async Task Create_NoItems_IsInvalid()
    {
        // Act
        var created = await CreateAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(created, FailureKind.Invalid, AiErrorCodes.FieldInvalid);
            Assert.That(IssueAt(created, "items"), Does.Contain("1 到 20"));
        }
    }

    [Test]
    public async Task Create_MoreThanTheLimit_IsInvalid()
    {
        // Act
        var created = await CreateAsync([.. Enumerable.Range(0, AiRules.MaxNewItemsPerProposal + 1).Select(i => new NewItemDraft("bug", $"錯誤 {i}"))]);

        // Assert
        Assert.That(IssueAt(created, "items"), Does.Contain("1 到 20"));
    }

    [Test]
    public async Task Create_UnknownType_ListsTheAvailableTypes()
    {
        // Act
        var created = await CreateAsync(new NewItemDraft("story", "x"));

        // Assert
        Assert.That(IssueAt(created, "items[0].type"), Does.Contain("epic（史詩）").And.Contain("task（任務）").And.Contain("bug（錯誤）"));
    }

    [Test]
    public async Task Create_ValueProblems_AreAllReportedAtOnceWithTheirPaths()
    {
        // Act
        var created = await CreateAsync(
            new NewItemDraft("bug", "  "),
            new NewItemDraft("bug", "標題\u0000") { Priority = "huge", StartDate = "12/01", DueDate = "2026-13-40", Estimate = -1m });

        // Assert
        var paths = created.Failure!.Details.Select(d => d.Path).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(paths, Is.SupersetOf(new[] { "items[0].title", "items[1].title", "items[1].priority", "items[1].startDate", "items[1].dueDate", "items[1].estimate" }));
        }
    }

    [Test]
    public async Task Create_TitleTooLongOrDescriptionTooLong_IsInvalid()
    {
        // Act
        var created = await CreateAsync(new NewItemDraft("bug", new string('字', WorkRules.MaxTitleLength + 1)) { Description = new string('字', WorkRules.MaxDescriptionLength + 1) });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(created, "items[0].title"), Does.Contain("最長"));
            Assert.That(IssueAt(created, "items[0].description"), Does.Contain("最長"));
        }
    }

    [Test]
    public async Task Create_AssigneeNotAMember_ListsTheMembersByNameAndId()
    {
        // Act
        var created = await CreateAsync(new NewItemDraft("bug", "x") { AssigneeId = Guid.NewGuid() });

        // Assert
        Assert.That(IssueAt(created, "items[0].assigneeId"), Does.Contain("不是這個專案的成員").And.Contain("王小明").And.Contain(ItemSamples.MemberId.ToString()));
    }

    [Test]
    public async Task Create_DisabledMemberIsNotListed()
    {
        // Arrange
        ((FakePeopleDirectory)H.Host.Get<PmTool.Identity.IPeopleDirectory>()).SetDisabled(H.OrganizationId, ItemSamples.MemberId, disabled: true);

        // Act
        var created = await CreateAsync(new NewItemDraft("bug", "x") { AssigneeId = Guid.NewGuid() });

        // Assert
        Assert.That(IssueAt(created, "items[0].assigneeId"), Does.Contain("沒有可以指派的成員").And.Not.Contain(ItemSamples.MemberId.ToString()));
    }

    [TestCase("Has Space")]
    [TestCase("UPPER")]
    [TestCase("this-reference-is-way-too-long")]
    public async Task Create_RefWithABadShape_IsInvalid(string reference)
    {
        // Act
        var created = await CreateAsync(Epic(reference: reference));

        // Assert
        Assert.That(IssueAt(created, "items[0].ref"), Does.Contain("ref 要是"));
    }

    [Test]
    public async Task Create_DuplicateRef_IsInvalidOnTheSecondOne()
    {
        // Act
        var created = await CreateAsync(Epic("甲"), Epic("乙"));

        // Assert
        Assert.That(IssueAt(created, "items[1].ref"), Does.Contain("重複"));
    }

    [Test]
    public async Task Create_ParentKeyAndParentRefTogether_IsInvalid()
    {
        // Act
        var created = await CreateAsync(Epic(), Task("x", parentRef: "e", parentKey: "WEB-12"));

        // Assert
        Assert.That(IssueAt(created, "items[1].parentRef"), Does.Contain("只能給一個"));
    }

    [Test]
    public async Task Create_ParentRefPointingAtALaterItem_SaysTheParentMustComeFirst()
    {
        // Act
        var created = await CreateAsync(Task("先寫的任務"), Epic());

        // Assert
        Assert.That(IssueAt(created, "items[0].parentRef"), Does.Contain("排在這一項後面"));
    }

    [Test]
    public async Task Create_ParentRefThatDoesNotExist_IsInvalid()
    {
        // Act
        var created = await CreateAsync(Epic(), Task("x", parentRef: "nope"));

        // Assert
        Assert.That(IssueAt(created, "items[1].parentRef"), Does.Contain("找不到 ref 是「nope」"));
    }

    [Test]
    public async Task Create_ParentTypeNotAllowedByTheBlueprint_ExplainsWhichTypesAreAllowed()
    {
        // Act
        var created = await CreateAsync(new NewItemDraft("bug", "只能在最上層") { Ref = "b" }, Task("掛在錯誤底下", parentRef: "b"), Epic("史詩") with { ParentRef = "b", Ref = "e2" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(created, "items[1].parentRef"), Does.Contain("不能掛在「錯誤」底下").And.Contain("只能掛在「史詩」"));
            Assert.That(IssueAt(created, "items[2].parentRef"), Does.Contain("只能放在最上層"));
        }
    }

    [Test]
    public async Task Create_ParentKeyOfAnExistingItem_IsCheckedAgainstTheBlueprint()
    {
        // Act
        var created = await CreateAsync(Task("掛在史詩", parentRef: null, parentKey: "web-12"), Task("掛在任務", parentRef: null, parentKey: "WEB-20"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.False);
            Assert.That(created.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "items[1].parentKey" }));
            Assert.That(IssueAt(created, "items[1].parentKey"), Does.Contain("不能掛在「任務」底下"));
        }
    }

    [Test]
    public async Task Create_ParentKeyNormalizedToTheItemsOwnKey()
    {
        // Act
        var created = await CreateAsync(Task("掛在史詩", parentRef: null, parentKey: " web-12 "));

        // Assert
        Assert.That(created.Value.Items.Single().ParentKey, Is.EqualTo("WEB-12"));
    }

    [Test]
    public async Task Create_ParentKeyNotFoundOrInAnotherProject_IsInvalid()
    {
        // Arrange
        H.WorkItems.Items["OPS-1"] = ItemSamples.Item("OPS-1", project: "OPS") with { Type = "epic" };

        // Act
        var created = await CreateAsync(Task("a", parentRef: null, parentKey: "WEB-99"), Task("b", parentRef: null, parentKey: "OPS-1"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(created, "items[0].parentKey"), Does.Contain("找不到上層項目"));
            Assert.That(IssueAt(created, "items[1].parentKey"), Does.Contain("找不到上層項目"));
        }
    }

    [Test]
    public async Task Create_CustomFieldProblems_AreReportedPerField()
    {
        // Act
        var created = await CreateAsync(
            new NewItemDraft("task", "x") { ParentKey = "WEB-12", Fields = new Dictionary<string, System.Text.Json.JsonElement> { ["category"] = Json(5), ["nope"] = Json("a") } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(created, "items[0].fields.category"), Does.Contain("值的 JSON 種類不對"));
            Assert.That(IssueAt(created, "items[0].fields.nope"), Does.Contain("沒有自訂欄位「nope」"));
        }
    }

    [Test]
    public async Task Create_Invalid_StoresNothing()
    {
        // Act
        await CreateAsync(new NewItemDraft("story", "x"));

        // Assert
        await using var db = await H.Host.CreateDbContextAsync();
        Assert.That(db.NewItemProposals.Count(), Is.Zero);
    }

    [Test]
    public async Task Get_AnotherPersonsProposal_IsNotFound()
    {
        // Arrange
        var created = await CreatedAsync(Epic());

        // Act
        var read = await Proposals.GetAsync(new Actor(H.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true), created.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalNotFound));
    }

    [Test]
    public async Task Get_ProjectNoLongerVisible_IsNotFound()
    {
        // Arrange
        var created = await CreatedAsync(Epic());
        H.Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var read = await Proposals.GetAsync(Me, created.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.NewItemProposalNotFound));
    }
}
