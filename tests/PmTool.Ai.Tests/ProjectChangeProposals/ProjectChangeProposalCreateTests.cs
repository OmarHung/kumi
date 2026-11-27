using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.PlanningProposals;
using PmTool.Work;

namespace PmTool.Ai.Tests.ProjectChangeProposals;

/// <summary>建立改專案建議：權限、名稱與說明的規則、祕密、太長的說明不能改、沒變就不能提。</summary>
[TestFixture]
public sealed class ProjectChangeProposalCreateTests : ProjectWriteTestBase
{
    private static readonly DateTimeOffset DescriptionTime = new(2027, 1, 5, 8, 0, 0, TimeSpan.Zero);

    private static ProjectChangeProposalDraft Draft(string? name = null, bool? archived = null, Optional<string?> description = default) =>
        new("WEB", name, archived, description);

    [Test]
    public async Task Create_NameArchiveAndDescription_StoresThemWithTheCurrentSnapshot()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription("# 舊說明", DescriptionTime, Manager);

        // Act
        var created = await ChangeCreator.CreateAsync(
            Me, Draft("  新名稱 ", archived: true, description: "# 新說明\n\n- 主機：example") with { Reason = "結案", ConversationId = Guid.NewGuid() }, CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
        var proposal = created.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposal.Name, Is.EqualTo("新名稱"));
            Assert.That(proposal.Archived, Is.True);
            Assert.That(proposal.ChangesDescription, Is.True);
            Assert.That(proposal.Description, Is.EqualTo("# 新說明\n\n- 主機：example"));
            Assert.That(proposal.Before, Is.EqualTo(new ProjectSnapshot("網站改版", false, "# 舊說明", DescriptionTime)));
            Assert.That(proposal.Reason, Is.EqualTo("結案"));
            Assert.That(proposal.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(Projects.Writes, Is.Empty);
            Assert.That(Descriptions.Sets, Is.Empty, "只建立建議，不改專案");
        }
    }

    [Test]
    public async Task Create_OnlyOneField_LeavesTheOthersUntouched()
    {
        // Act
        var renamed = await ChangeCreator.CreateAsync(Me, Draft(name: "改名"), CancellationToken.None);
        var archived = await ChangeCreator.CreateAsync(Me, Draft(archived: true), CancellationToken.None);
        var cleared = await ChangeCreator.CreateAsync(Me, Draft(), CancellationToken.None);
        Descriptions.Current = new ProjectDescription("有說明", DescriptionTime, Manager);
        var clearsDescription = await ChangeCreator.CreateAsync(Me, Draft(description: new Optional<string?>(null)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(renamed.Value.Archived, Is.Null);
            Assert.That(renamed.Value.ChangesDescription, Is.False);
            Assert.That(archived.Value.Name, Is.Null);
            Assert.That(cleared.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(clearsDescription.Value.ChangesDescription, Is.True);
            Assert.That(clearsDescription.Value.Description, Is.Null);
        }
    }

    [Test]
    public async Task Create_WhitespaceDescription_MeansClearing()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription("有說明", DescriptionTime, Manager);

        // Act
        var created = await ChangeCreator.CreateAsync(Me, Draft(description: "  \n "), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Value.ChangesDescription, Is.True);
            Assert.That(created.Value.Description, Is.Null);
        }
    }

    [Test]
    public async Task Create_WithoutProjectManage_IsForbiddenAndNamesTheManagers()
    {
        // Arrange
        UseProject(canManage: false);

        // Act
        var created = await ChangeCreator.CreateAsync(Me, Draft(name: "改名"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
            Assert.That(created.Failure?.Message, Does.Contain("甲").And.Contain("組織管理員"));
        }
    }

    [Test]
    public async Task Create_ProjectNotVisible_ReturnsTheProjectNotFoundAsIs()
    {
        // Arrange
        Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var created = await ChangeCreator.CreateAsync(Me, Draft(name: "改名"), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task Create_ArchivedProject_CanStillBeUnarchivedRenamedAndDescribed()
    {
        // Arrange
        UseProject(archived: true);

        // Act
        var created = await ChangeCreator.CreateAsync(Me, Draft(name: "改名", archived: false, description: "新說明"), CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
        Assert.That(created.Value.Before.Archived, Is.True);
    }

    [Test]
    public async Task Create_NothingGiven_IsRejectedWithAnEmptyPath()
    {
        // Act
        var created = await ChangeCreator.CreateAsync(Me, Draft(), CancellationToken.None);

        // Assert
        Assert.That(IssueAt(created, string.Empty), Does.Contain("至少"));
    }

    [Test]
    public async Task Create_EverythingSameAsNow_IsRejectedWithAnEmptyPath()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription("說明", DescriptionTime, Manager);

        // Act
        var created = await ChangeCreator.CreateAsync(Me, Draft(name: "網站改版", archived: false, description: "說明"), CancellationToken.None);

        // Assert
        Assert.That(IssueAt(created, string.Empty), Does.Contain("一樣"));
    }

    [Test]
    public async Task Create_SomeFieldsSameAsNow_PointsAtThoseFields()
    {
        // Act
        var created = await ChangeCreator.CreateAsync(Me, Draft(name: "網站改版", archived: true), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "name" }));
            Assert.That(IssueAt(created, "name"), Does.Contain("一樣"));
        }
    }

    [Test]
    public async Task Create_NameRules_BlankTooLongAndControlCharacters()
    {
        // Act
        var blank = await ChangeCreator.CreateAsync(Me, Draft(name: "   "), CancellationToken.None);
        var tooLong = await ChangeCreator.CreateAsync(Me, Draft(name: new string('甲', WorkRules.MaxProjectNameLength + 1)), CancellationToken.None);
        var control = await ChangeCreator.CreateAsync(Me, Draft(name: "換\n行"), CancellationToken.None);
        var exact = await ChangeCreator.CreateAsync(Me, Draft(name: new string('甲', WorkRules.MaxProjectNameLength)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(blank, "name"), Does.Contain("1 到 100"));
            Assert.That(IssueAt(tooLong, "name"), Does.Contain("1 到 100"));
            Assert.That(IssueAt(control, "name"), Does.Contain("控制字元"));
            Assert.That(exact.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Create_DescriptionRules_TooLongControlCharactersAndSecrets()
    {
        // Arrange
        var secret = "-----BEGIN RSA PRIVATE KEY-----\nabc\n-----END RSA PRIVATE KEY-----";

        // Act
        var tooLong = await ChangeCreator.CreateAsync(Me, Draft(description: new string('字', ProjectDescriptionRules.MaxTextLength + 1)), CancellationToken.None);
        var control = await ChangeCreator.CreateAsync(Me, Draft(description: "壞\u0001字"), CancellationToken.None);
        var key = await ChangeCreator.CreateAsync(Me, Draft(description: secret), CancellationToken.None);
        var tabsAndNewlines = await ChangeCreator.CreateAsync(Me, Draft(description: "a\tb\r\nc"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(tooLong, "description"), Does.Contain("最多 50000"));
            Assert.That(IssueAt(control, "description"), Does.Contain("控制字元"));
            Assert.That(IssueAt(key, "description"), Does.Contain("私鑰"));
            Assert.That(tabsAndNewlines.Succeeded, Is.True, "換行與 Tab 可以");
            Assert.That(tabsAndNewlines.Value.Description, Is.EqualTo("a\tb\r\nc"), "照原樣存，和 SetAsync 一樣");
        }
    }

    [Test]
    public async Task Create_CurrentDescriptionTooLongForTheAssistantToRead_CannotChangeTheDescriptionButCanRename()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription(new string('字', AiRules.MaxEditableProjectDescriptionLength + 1), DescriptionTime, Manager);

        // Act
        var describe = await ChangeCreator.CreateAsync(Me, Draft(description: "新說明"), CancellationToken.None);
        var rename = await ChangeCreator.CreateAsync(Me, Draft(name: "改名"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(describe, "description"), Does.Contain("讀不到全文"));
            Assert.That(rename.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Create_ReasonTooLong_IsRejectedTogetherWithOtherProblems()
    {
        // Act
        var created = await ChangeCreator.CreateAsync(Me, Draft(name: "  ") with { Reason = new string('因', 201) }, CancellationToken.None);

        // Assert
        Assert.That(created.Failure!.Details.Select(d => d.Path), Is.EquivalentTo(new[] { "name", "reason" }));
    }

    [Test]
    public async Task Create_OnlyTheCreatorCanReadItBack()
    {
        // Arrange
        var created = await ChangeCreator.CreateAsync(Me, Draft(name: "改名"), CancellationToken.None);
        var other = new Actor(H.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);

        // Act
        var mine = await ChangeService.GetAsync(Me, created.Value.Id, CancellationToken.None);
        var theirs = await ChangeService.GetAsync(other, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mine.Value.Name, Is.EqualTo("改名"));
            Assert.That(theirs.Failure?.Code, Is.EqualTo(AiErrorCodes.ProjectChangeProposalNotFound));
        }
    }
}
