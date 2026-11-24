using PmTool.Abstractions;
using PmTool.Work.Projects;

namespace PmTool.Work.Tests.Projects;

/// <summary>說明格式的新增、修改、刪除、預設與上限（internal/HANDOFF-project-info.md 第 5.3 節）。</summary>
[TestFixture]
public sealed class DescriptionTemplateTests
{
    private const string BuiltInId = "web_project";
    private const string BuiltInName = "網站案子";

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private IProjectDescriptionService Service => _host.Descriptions;

    private static DescriptionTemplateInput Input(string name = "維運", string body = "## 主機", bool isDefault = false) =>
        new(name, body, isDefault);

    private async Task<DescriptionTemplate> CreateAsync(Actor actor, string name, bool isDefault = false)
    {
        var result = await Service.CreateTemplateAsync(actor, Input(name, $"## {name}", isDefault), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    [Test]
    public async Task ListTemplatesAsync_NoOwnTemplates_ReturnsOnlyTheBuiltInAsDefault()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var list = await Service.ListTemplatesAsync(scenario.Person(), CancellationToken.None);

        // Assert
        Assert.That(list.Select(t => (t.Id, t.IsBuiltIn, t.IsDefault)), Is.EqualTo(new[] { (BuiltInId, true, true) }));
    }

    [Test]
    public async Task CreateTemplateAsync_Admin_IsReturnedAndListedBeforeTheBuiltIn()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var created = await Service.CreateTemplateAsync(scenario.Admin, Input("  維運  ", "## 主機\r\n- a"), CancellationToken.None);
        var list = await Service.ListTemplatesAsync(scenario.Person(), CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Guid.TryParse(created.Value.Id, out _), Is.True);
            Assert.That(created.Value.Name, Is.EqualTo("維運"));
            Assert.That(created.Value.Body, Is.EqualTo("## 主機\n- a"));
            Assert.That(created.Value.IsBuiltIn, Is.False);
            Assert.That(created.Value.IsDefault, Is.False);
            Assert.That(list.Select(t => t.Id), Is.EqualTo(new[] { created.Value.Id, BuiltInId }));
            Assert.That(list.Single(t => t.IsDefault).Id, Is.EqualTo(BuiltInId));
        }
    }

    [Test]
    public async Task CreateTemplateAsync_NotOrganizationAdmin_IsForbiddenEvenWithBadContent()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await Service.CreateTemplateAsync(scenario.Person(), Input("", ""), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
    }

    [Test]
    public async Task ListTemplatesAsync_DefaultFirstThenByName()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario.Admin, "乙");
        var defaulted = await CreateAsync(scenario.Admin, "丙", isDefault: true);
        await CreateAsync(scenario.Admin, "甲");

        // Act
        var list = await Service.ListTemplatesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(list.Select(t => t.Name), Is.EqualTo(new[] { "丙", "乙", "甲", BuiltInName }));
            Assert.That(list.Select(t => t.IsDefault), Is.EqualTo(new[] { true, false, false, false }));
            Assert.That(list[0].Id, Is.EqualTo(defaulted.Id));
        }
    }

    [Test]
    public async Task CreateTemplateAsync_NewDefault_ClearsThePreviousDefault()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var first = await CreateAsync(scenario.Admin, "甲", isDefault: true);

        // Act
        var second = await CreateAsync(scenario.Admin, "乙", isDefault: true);
        var list = await Service.ListTemplatesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(list.Where(t => t.IsDefault).Select(t => t.Id), Is.EqualTo(new[] { second.Id }));
            Assert.That(list.Single(t => t.Id == first.Id).IsDefault, Is.False);
        }
    }

    [Test]
    public async Task UpdateTemplateAsync_SetDefaultFalse_FallsBackToTheBuiltIn()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var template = await CreateAsync(scenario.Admin, "甲", isDefault: true);

        // Act
        var updated = await Service.UpdateTemplateAsync(
            scenario.Admin, Guid.Parse(template.Id), Input("甲", "## 新內容", isDefault: false), CancellationToken.None);
        var list = await Service.ListTemplatesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(updated.Succeeded, Is.True, () => updated.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Value.Body, Is.EqualTo("## 新內容"));
            Assert.That(list.Where(t => t.IsDefault).Select(t => t.Id), Is.EqualTo(new[] { BuiltInId }));
        }
    }

    [Test]
    public async Task UpdateTemplateAsync_SetDefaultTrue_MovesTheDefault()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var first = await CreateAsync(scenario.Admin, "甲", isDefault: true);
        var second = await CreateAsync(scenario.Admin, "乙");

        // Act
        var updated = await Service.UpdateTemplateAsync(
            scenario.Admin, Guid.Parse(second.Id), Input("乙", "## x", isDefault: true), CancellationToken.None);
        var list = await Service.ListTemplatesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(updated.Succeeded, Is.True, () => updated.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(list.Where(t => t.IsDefault).Select(t => t.Id), Is.EqualTo(new[] { second.Id }));
            Assert.That(list.Single(t => t.Id == first.Id).IsDefault, Is.False);
        }
    }

    [Test]
    public async Task DeleteTemplateAsync_Default_ReturnsItAndTheBuiltInBecomesDefault()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var template = await CreateAsync(scenario.Admin, "甲", isDefault: true);

        // Act
        var deleted = await Service.DeleteTemplateAsync(scenario.Admin, Guid.Parse(template.Id), CancellationToken.None);
        var list = await Service.ListTemplatesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Value, Is.EqualTo(template));
            Assert.That(list.Select(t => (t.Id, t.IsDefault)), Is.EqualTo(new[] { (BuiltInId, true) }));
        }
    }

    [Test]
    public async Task UpdateAndDelete_NotOrganizationAdmin_AreForbidden()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var template = await CreateAsync(scenario.Admin, "甲");
        var id = Guid.Parse(template.Id);

        // Act
        var update = await Service.UpdateTemplateAsync(scenario.Person(), id, Input(), CancellationToken.None);
        var delete = await Service.DeleteTemplateAsync(scenario.Person(), id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(update.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
            Assert.That(delete.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
        }
    }

    [Test]
    public async Task UpdateAndDelete_UnknownOrOtherOrganizationsTemplate_AreNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var other = new Scenario(_host);
        var foreign = await CreateAsync(other.Admin, "別人的");

        // Act
        var results = new[]
        {
            await Service.UpdateTemplateAsync(scenario.Admin, Guid.Parse(foreign.Id), Input(), CancellationToken.None),
            await Service.DeleteTemplateAsync(scenario.Admin, Guid.Parse(foreign.Id), CancellationToken.None),
            await Service.UpdateTemplateAsync(scenario.Admin, Guid.NewGuid(), Input(), CancellationToken.None),
            await Service.DeleteTemplateAsync(scenario.Admin, Guid.NewGuid(), CancellationToken.None),
        };
        var stillThere = await Service.ListTemplatesAsync(other.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(r => r.Failure?.Code), Is.All.EqualTo(WorkErrorCodes.DescriptionTemplateNotFound));
            Assert.That(results.Select(r => r.Failure?.Kind), Is.All.EqualTo(FailureKind.NotFound));
            Assert.That(stillThere.Select(t => t.Id), Does.Contain(foreign.Id));
        }
    }

    [Test]
    public async Task Create_NameTaken_ComparesCaseInsensitivelyAndAgainstTheBuiltIn()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario.Admin, "abc");

        // Act
        var sameCase = await Service.CreateTemplateAsync(scenario.Admin, Input("ABC"), CancellationToken.None);
        var builtIn = await Service.CreateTemplateAsync(scenario.Admin, Input(" 網站案子 "), CancellationToken.None);
        var otherOrganization = await Service.CreateTemplateAsync(new Scenario(_host).Admin, Input("ABC"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameCase.Failure?.Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateNameTaken));
            Assert.That(sameCase.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(builtIn.Failure?.Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateNameTaken));
            Assert.That(otherOrganization.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Update_NameTaken_IgnoresItselfButNotOthers()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var first = await CreateAsync(scenario.Admin, "甲");
        await CreateAsync(scenario.Admin, "Beta");

        // Act
        var itself = await Service.UpdateTemplateAsync(scenario.Admin, Guid.Parse(first.Id), Input("甲", "## x"), CancellationToken.None);
        var other = await Service.UpdateTemplateAsync(scenario.Admin, Guid.Parse(first.Id), Input("beta", "## x"), CancellationToken.None);
        var builtIn = await Service.UpdateTemplateAsync(scenario.Admin, Guid.Parse(first.Id), Input("網站案子", "## x"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(itself.Succeeded, Is.True);
            Assert.That(other.Failure?.Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateNameTaken));
            Assert.That(builtIn.Failure?.Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateNameTaken));
        }
    }

    [Test]
    public async Task Create_AtTheLimit_IsRefusedAndDeletingFreesASlot()
    {
        // Arrange
        var scenario = new Scenario(_host);
        DescriptionTemplate last = null!;
        for (var i = 0; i < ProjectDescriptionRules.MaxTemplatesPerOrganization; i++)
        {
            last = await CreateAsync(scenario.Admin, $"格式 {i}");
        }

        // Act
        var refused = await Service.CreateTemplateAsync(scenario.Admin, Input("多一份"), CancellationToken.None);
        await Service.DeleteTemplateAsync(scenario.Admin, Guid.Parse(last.Id), CancellationToken.None);
        var accepted = await Service.CreateTemplateAsync(scenario.Admin, Input("多一份"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused.Failure?.Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateLimitReached));
            Assert.That(refused.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(accepted.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Create_ValidationProblems_AreAllReportedWithTheirPaths()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var tooLongName = new string('名', ProjectDescriptionRules.MaxTemplateNameLength + 1);

        // Act
        var empty = await Service.CreateTemplateAsync(scenario.Admin, Input(" ", "  \n"), CancellationToken.None);
        var tooLong = await Service.CreateTemplateAsync(scenario.Admin, Input(tooLongName, "x"), CancellationToken.None);
        var lineBreakName = await Service.CreateTemplateAsync(scenario.Admin, Input("a\nb", "x"), CancellationToken.None);
        var invisibleName = await Service.CreateTemplateAsync(scenario.Admin, Input("a\u200Bb", "x"), CancellationToken.None);
        var longBody = await Service.CreateTemplateAsync(
            scenario.Admin, Input("a", new string('x', ProjectDescriptionRules.MaxTextLength + 1)), CancellationToken.None);
        var control = await Service.CreateTemplateAsync(scenario.Admin, Input("a", "x\0y"), CancellationToken.None);
        var key = await Service.CreateTemplateAsync(scenario.Admin, Input("a", "貼了 -----BEGIN RSA PRIVATE KEY-----"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                empty.Failure!.Details.Select(d => (d.Code, d.Path)),
                Is.EquivalentTo(new[]
                {
                    (WorkErrorCodes.DescriptionTemplateNameRequired, "name"),
                    (WorkErrorCodes.DescriptionTemplateBodyRequired, "body"),
                }));
            Assert.That(tooLong.Failure!.Details.Single().Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateNameTooLong));
            Assert.That(lineBreakName.Failure!.Details.Single().Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateNameRequired));
            Assert.That(invisibleName.Failure!.Details.Single().Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateNameRequired));
            Assert.That(longBody.Failure!.Details.Single().Code, Is.EqualTo(WorkErrorCodes.ProjectDescriptionTooLong));
            Assert.That(longBody.Failure!.Details.Single().Path, Is.EqualTo("body"));
            Assert.That(control.Failure!.Details.Single().Code, Is.EqualTo(WorkErrorCodes.ProjectDescriptionInvalid));
            Assert.That(control.Failure!.Details.Single().Path, Is.EqualTo("body"));
            Assert.That(key.Failure!.Details.Single().Code, Is.EqualTo(WorkErrorCodes.ProjectDescriptionSecret));
            Assert.That(key.Failure!.Details.Single().Path, Is.EqualTo("body"));
        }
    }

    [Test]
    public async Task Update_BadContent_IsRefusedAndKeepsTheOldTemplate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var template = await CreateAsync(scenario.Admin, "甲");

        // Act
        var result = await Service.UpdateTemplateAsync(
            scenario.Admin, Guid.Parse(template.Id), Input("甲", "-----BEGIN PRIVATE KEY-----"), CancellationToken.None);
        var list = await Service.ListTemplatesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Details.Single().Code, Is.EqualTo(WorkErrorCodes.ProjectDescriptionSecret));
            Assert.That(list.Single(t => t.Id == template.Id), Is.EqualTo(template));
        }
    }

    [Test]
    public async Task Create_ConcurrentDefaults_LeavesExactlyOneDefault()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Service.CreateTemplateAsync(scenario.Admin, Input($"格式 {i}", "x", isDefault: true), CancellationToken.None)));
        var list = await Service.ListTemplatesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.All(r => r.Succeeded), Is.True, () => string.Join("；", results.Select(r => r.Failure?.Message)));
            Assert.That(list.Count(t => t.IsDefault), Is.EqualTo(1));
            Assert.That(list.Single(t => t.IsDefault).IsBuiltIn, Is.False);
        }
    }

    [Test]
    public async Task Create_ConcurrentAtTheLimit_OnlyOneSucceeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        for (var i = 0; i < ProjectDescriptionRules.MaxTemplatesPerOrganization - 1; i++)
        {
            await CreateAsync(scenario.Admin, $"格式 {i}");
        }

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            Service.CreateTemplateAsync(scenario.Admin, Input($"搶 {i}"), CancellationToken.None)));
        var list = await Service.ListTemplatesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(
                results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code),
                Is.All.EqualTo(WorkErrorCodes.DescriptionTemplateLimitReached));
            Assert.That(list.Count(t => !t.IsBuiltIn), Is.EqualTo(ProjectDescriptionRules.MaxTemplatesPerOrganization));
        }
    }

    [Test]
    public async Task Create_ConcurrentSameName_OnlyOneSucceedsAndTheOthersAreNameTaken()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            Service.CreateTemplateAsync(scenario.Admin, Input(i % 2 == 0 ? "同名" : "同名".ToUpperInvariant()), CancellationToken.None)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(
                results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code),
                Is.All.EqualTo(WorkErrorCodes.DescriptionTemplateNameTaken));
        }
    }

    [Test]
    public async Task CreateAsync_ProjectWithDescription_StoresItWithCreatorAndTime()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var created = await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("WEB", "官網", Scenario.SimpleTasks) { Description = "## 網址\r\n- 正式" }, CancellationToken.None);
        var description = await Service.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(description.Value.Text, Is.EqualTo("## 網址\n- 正式"));
            Assert.That(description.Value.UpdatedBy, Is.EqualTo(scenario.Admin.PersonId));
            Assert.That(description.Value.UpdatedAt, Is.EqualTo(created.Value.CreatedAt));
        }
    }

    [TestCase(null)]
    [TestCase("  \n ")]
    public async Task CreateAsync_ProjectWithBlankDescription_WritesNothing(string? blank)
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("WEB", "官網", Scenario.SimpleTasks) { Description = blank }, CancellationToken.None);
        var description = await Service.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(description.Value, Is.EqualTo(ProjectDescription.Empty));
    }

    [Test]
    public async Task CreateAsync_ProjectWithPrivateKey_IsRefusedTogetherWithOtherProblemsAndNothingIsCreated()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Projects.CreateAsync(
            scenario.Admin,
            new NewProject("web", " ", Scenario.SimpleTasks) { Description = "-----BEGIN OPENSSH PRIVATE KEY-----" },
            CancellationToken.None);
        var valid = await _host.Projects.CreateAsync(
            scenario.Admin,
            new NewProject("WEB", "官網", Scenario.SimpleTasks) { Description = "-----BEGIN OPENSSH PRIVATE KEY-----" },
            CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Failure!.Details.Select(d => d.Path),
                Is.EquivalentTo(new[] { "key", "name", "description" }));
            Assert.That(valid.Failure!.Details.Single().Path, Is.EqualTo("description"));
            Assert.That(valid.Failure!.Details.Single().Code, Is.EqualTo(WorkErrorCodes.ProjectDescriptionSecret));
            Assert.That(detail.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
        }
    }

    [Test]
    public async Task CreateAsync_ProjectKeyTakenWithADescription_KeepsTheOriginalProjectAndItsDescription()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("WEB", "原本", Scenario.SimpleTasks) { Description = "## 原本的說明" }, CancellationToken.None);

        // Act
        var second = await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("WEB", "另一個", Scenario.SimpleTasks) { Description = "## 另一份說明" }, CancellationToken.None);
        var description = await Service.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        var projects = await _host.Projects.ListAsync(scenario.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectKeyTaken));
            Assert.That(description.Value.Text, Is.EqualTo("## 原本的說明"));
            Assert.That(projects.Select(p => p.Name), Is.EqualTo(new[] { "原本" }));
        }
    }

    [Test]
    public void ConflictFor_DefaultIndexViolation_IsABusyConflictNotAnException()
    {
        // Arrange：排隊之後正常路徑碰不到這個索引，所以只驗證撞到時的轉換。
        var exception = Violation(Persistence.WorkDbContext.DescriptionTemplateDefaultIndex);

        // Act
        var failure = DescriptionTemplateChecks.ConflictFor(exception, "維運");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(failure?.Code, Is.EqualTo(WorkErrorCodes.ChangeSetBusy));
        }
    }

    [Test]
    public void ConflictFor_NameIndexViolation_IsNameTaken()
    {
        // Arrange
        var exception = new InvalidOperationException("包起來", Violation(Persistence.WorkDbContext.DescriptionTemplateNameIndex));

        // Act
        var failure = DescriptionTemplateChecks.ConflictFor(exception, "維運");

        // Assert
        Assert.That(failure?.Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateNameTaken));
    }

    [Test]
    public void ConflictFor_OtherException_IsNotConverted()
    {
        // Arrange
        var exception = Violation("ux_something_else");

        // Act
        var failure = DescriptionTemplateChecks.ConflictFor(exception, "維運");

        // Assert
        Assert.That(failure, Is.Null);
    }

    private static Npgsql.PostgresException Violation(string constraint) => new(
        "duplicate key", "ERROR", "ERROR", Npgsql.PostgresErrorCodes.UniqueViolation, constraintName: constraint);
}
