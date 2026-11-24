using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

[TestFixture]
public sealed class ProjectCreationTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_OrganizationAdmin_StoresVersionOneAndMakesCreatorAdmin()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var created = await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("WEB", "官網改版", Scenario.AgencyProject), CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Value.Key, Is.EqualTo("WEB"));
            Assert.That(created.Value.Name, Is.EqualTo("官網改版"));
            Assert.That(created.Value.BlueprintVersion, Is.EqualTo(1));
            Assert.That(created.Value.MyRole, Is.EqualTo("admin"));
            Assert.That(created.Value.CreatedAt, Is.EqualTo(WorkTestHost.StartTime));
            Assert.That(detail.Value.Project, Is.EqualTo(created.Value));
            Assert.That(detail.Value.Members, Is.EqualTo(new[] { new ProjectMember(scenario.Admin.PersonId, "admin") }));
        }
    }

    [Test]
    public async Task CreateAsync_FromTemplate_ReplacesMetadataWithTheProjects()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        await scenario.CreateProjectAsync("SHOP2", Scenario.AgencyProject);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "SHOP2", CancellationToken.None);

        // Assert：metadata 換成這個專案的，其餘照模板。
        var blueprint = detail.Value.Blueprint;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blueprint.Metadata.Key, Is.EqualTo("shop2"));
            Assert.That(blueprint.Metadata.Name, Is.EqualTo("範例專案 SHOP2"));
            Assert.That(blueprint.Metadata.BasedOn!.Template, Is.EqualTo(Scenario.AgencyProject));
            Assert.That(blueprint.Metadata.BasedOn.Version, Is.EqualTo("1.0.0"));
            Assert.That(blueprint.ItemTypes.Select(t => t.Key), Is.EqualTo(new[] { "requirement", "change_request", "task", "subtask" }));
            Assert.That(blueprint.Roles.Select(r => r.Key), Is.EqualTo(new[] { "project_manager", "client" }));
        }
    }

    [TestCase("web")]
    [TestCase("W")]
    [TestCase("1WEB")]
    [TestCase("WEB-1")]
    [TestCase("ABCDEFGHIJK")]
    [TestCase("WEB\n")]
    [TestCase("")]
    public async Task CreateAsync_MalformedKey_IsInvalid(string key)
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Projects.CreateAsync(scenario.Admin, new NewProject(key, "官網改版", Scenario.SimpleTasks), CancellationToken.None);

        // Assert
        AssertInvalid(result, WorkErrorCodes.ProjectKeyInvalid, "key");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("第一行\n第二行")]
    public async Task CreateAsync_MissingOrUnusableName_IsInvalid(string name)
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Projects.CreateAsync(scenario.Admin, new NewProject("WEB", name, Scenario.SimpleTasks), CancellationToken.None);

        // Assert
        AssertInvalid(result, WorkErrorCodes.ProjectNameRequired, "name");
    }

    [Test]
    public async Task CreateAsync_NameLongerThanLimit_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var name = new string('名', WorkRules.MaxProjectNameLength + 1);

        // Act
        var result = await _host.Projects.CreateAsync(scenario.Admin, new NewProject("WEB", name, Scenario.SimpleTasks), CancellationToken.None);

        // Assert
        AssertInvalid(result, WorkErrorCodes.ProjectNameTooLong, "name");
    }

    [Test]
    public async Task CreateAsync_NameWithSurroundingSpaces_StoresTrimmedName()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var name = "  " + new string('名', WorkRules.MaxProjectNameLength) + "  ";

        // Act
        var result = await _host.Projects.CreateAsync(scenario.Admin, new NewProject("WEB", name, Scenario.SimpleTasks), CancellationToken.None);

        // Assert
        Assert.That(result.Value.Name, Is.EqualTo(name.Trim()));
    }

    [Test]
    public async Task CreateAsync_UnknownTemplate_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Projects.CreateAsync(scenario.Admin, new NewProject("WEB", "官網改版", "no_such_template"), CancellationToken.None);

        // Assert
        AssertInvalid(result, WorkErrorCodes.TemplateNotFound, "template");
    }

    [Test]
    public async Task CreateAsync_SeveralProblems_ReportsAllOfThem()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Projects.CreateAsync(scenario.Admin, new NewProject("web", "", "nope"), CancellationToken.None);

        // Assert
        Assert.That(
            result.Failure!.Details.Select(d => d.Code),
            Is.EqualTo(new[] { WorkErrorCodes.ProjectKeyInvalid, WorkErrorCodes.ProjectNameRequired, WorkErrorCodes.TemplateNotFound }));
    }

    [Test]
    public async Task CreateAsync_KeyAlreadyUsedInOrganization_IsConflict()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.CreateAsync(scenario.Admin, new NewProject("WEB", "另一個", Scenario.AgencyProject), CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Conflict));
        Assert.That(result.Failure.Code, Is.EqualTo(WorkErrorCodes.ProjectKeyTaken));
    }

    [Test]
    public async Task CreateAsync_SameKeyInAnotherOrganization_Succeeds()
    {
        // Arrange
        await new Scenario(_host).CreateProjectAsync("WEB");
        var other = new Scenario(_host);

        // Act
        var result = await _host.Projects.CreateAsync(other.Admin, new NewProject("WEB", "別的組織", Scenario.SimpleTasks), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task CreateAsync_SameKeyAtTheSameTime_OnlyOneSucceeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var requests = Enumerable.Range(0, 6).Select(_ => _host.NewRequest()).ToArray();

        // Act
        var results = await Task.WhenAll(requests.Select(r =>
            r.Projects.CreateAsync(scenario.Admin, new NewProject("RACE", "同時開", Scenario.SimpleTasks), CancellationToken.None)));
        foreach (var request in requests)
        {
            await request.Scope.DisposeAsync();
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Has.All.EqualTo(WorkErrorCodes.ProjectKeyTaken));
        }
    }

    [TestCase(Scenario.AgencyProject)]
    [TestCase(Scenario.SimpleTasks)]
    public async Task CreateAsync_EachTemplate_StoresABlueprintThatPassesValidation(string template)
    {
        // Arrange
        var scenario = new Scenario(_host);
        var validator = PmTool.Blueprint.BlueprintValidator.CreateDefault();

        // Act：讀資料庫裡存的文件本身，不經過快取。jsonb 會重排屬性、去掉空白，之後每次讀回來的都是這個樣子。
        await scenario.CreateProjectAsync("TPL", template);
        var stored = await StoredBlueprints.ReadCurrentAsync(_host, scenario.OrganizationId, "TPL");
        var result = validator.ValidateBlueprint(stored, PmTool.Blueprint.DocumentFormat.Json);

        // Assert
        Assert.That(result.Report.Issues, Is.Empty);
        Assert.That(result.Blueprint!.Metadata.Key, Is.EqualTo("tpl"));
    }

    [Test]
    public async Task CreateAsync_NotOrganizationAdmin_IsForbidden()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Projects.CreateAsync(
            scenario.Person(), new NewProject("WEB", "官網改版", Scenario.SimpleTasks), CancellationToken.None);

        // Assert
        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
        Assert.That(result.Failure.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
    }

    private static void AssertInvalid<T>(Result<T> result, string code, string path)
    {
        Assert.That(result.Failure, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure.Code, Is.EqualTo(code));
            Assert.That(result.Failure.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (code, path) }));
            Assert.That(result.Failure.Message, Is.Not.Empty);
        }
    }
}
