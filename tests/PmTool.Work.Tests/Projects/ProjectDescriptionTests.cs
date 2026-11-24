using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

/// <summary>專案說明：讀寫、權限、內容檢查（internal/HANDOFF-project-info.md 第 4 節）。</summary>
[TestFixture]
public sealed class ProjectDescriptionTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task GetAsync_NeverWritten_ReturnsEmpty()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Descriptions.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(ProjectDescription.Empty));
    }

    [Test]
    public async Task SetAsync_Text_IsReadBackWithTimeAndPersonAndKeepsWhitespace()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var text = "\n## 網址\n\t- 正式：https://www.example.com  \n";

        // Act
        var written = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", text, CancellationToken.None);
        var read = await _host.Descriptions.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Value, Is.EqualTo(written.Value));
            Assert.That(read.Value.Text, Is.EqualTo(text));
            Assert.That(read.Value.UpdatedBy, Is.EqualTo(scenario.Admin.PersonId));
            Assert.That(read.Value.UpdatedAt, Is.Not.Null);
        }
    }

    [Test]
    public async Task SetAsync_CrLf_IsStoredAsLf()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "a\r\nb\rc", CancellationToken.None);

        // Assert
        Assert.That(result.Value.Text, Is.EqualTo("a\nb\nc"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  \n\t ")]
    public async Task SetAsync_NullOrBlank_ClearsButRecordsTimeAndPerson(string? blank)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var manager = await scenario.MemberAsync("WEB", "admin");
        await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "內容", CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var cleared = await _host.Descriptions.SetAsync(manager, "WEB", blank, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cleared.Value.Text, Is.Null);
            Assert.That(cleared.Value.UpdatedBy, Is.EqualTo(manager.PersonId));
            Assert.That(cleared.Value.UpdatedAt, Is.Not.Null);
            Assert.That((await _host.Descriptions.GetAsync(scenario.Admin, "WEB", CancellationToken.None)).Value, Is.EqualTo(cleared.Value));
        }
    }

    [TestCase(null)]
    [TestCase("  \n ")]
    public async Task SetAsync_ClearingWhenNeverWritten_WritesNothingAndReturnsEmpty(string? blank)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", blank, CancellationToken.None);
        var read = await _host.Descriptions.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo(ProjectDescription.Empty));
            Assert.That(read.Value, Is.EqualTo(ProjectDescription.Empty));
        }
    }

    [Test]
    public async Task SetAsync_SameText_KeepsTimeAndPerson()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var manager = await scenario.MemberAsync("WEB", "admin");
        var first = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "內容", CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var second = await _host.Descriptions.SetAsync(manager, "WEB", "內容", CancellationToken.None);

        // Assert
        Assert.That(second.Value, Is.EqualTo(first.Value));
    }

    [Test]
    public async Task SetAsync_ChangedText_UpdatesTimeAndPerson()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var manager = await scenario.MemberAsync("WEB", "admin");
        var first = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "內容", CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var second = await _host.Descriptions.SetAsync(manager, "WEB", "新內容", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Value.Text, Is.EqualTo("新內容"));
            Assert.That(second.Value.UpdatedBy, Is.EqualTo(manager.PersonId));
            Assert.That(second.Value.UpdatedAt, Is.GreaterThan(first.Value.UpdatedAt!.Value));
        }
    }

    [Test]
    public async Task SetAsync_TwoWritesAtTheSameTime_BothSucceedAndOneWins()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            await using var scope = _host.Root.CreateAsyncScope();
            var descriptions = scope.ServiceProvider.GetRequiredService<IProjectDescriptionService>();
            return await descriptions.SetAsync(scenario.Admin, "WEB", $"內容 {index}", CancellationToken.None);
        }));
        var read = await _host.Descriptions.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.All(r => r.Succeeded), Is.True, () => string.Join(",", results.Select(r => r.Failure?.Message)));
            Assert.That(read.Value.Text, Does.StartWith("內容 "));
        }
    }

    [Test]
    public async Task GetAsync_ContributorWhoOnlySeesOwnItems_CanReadButNotWrite()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "主機：example-host", CancellationToken.None);

        // Act
        var read = await _host.Descriptions.GetAsync(contributor, "WEB", CancellationToken.None);
        var write = await _host.Descriptions.SetAsync(contributor, "WEB", "改", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Value.Text, Is.EqualTo("主機：example-host"));
            Assert.That((write.Failure?.Kind, write.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.Forbidden)));
        }
    }

    [Test]
    public async Task SetAsync_EditorWithoutProjectManage_IsForbiddenAndNothingChanges()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var write = await _host.Descriptions.SetAsync(editor, "WEB", "改", CancellationToken.None);
        var read = await _host.Descriptions.GetAsync(editor, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
            Assert.That(read.Value, Is.EqualTo(ProjectDescription.Empty));
        }
    }

    [Test]
    public async Task SetAsync_ForbiddenActorWithBadText_GetsForbiddenNotAValidationMessage()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var write = await _host.Descriptions.SetAsync(editor, "WEB", "a\0b", CancellationToken.None);

        // Assert
        Assert.That(write.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
    }

    [Test]
    public async Task GetAndSetAsync_NotAMember_AreNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var outsider = scenario.Person();

        // Act
        var read = await _host.Descriptions.GetAsync(outsider, "WEB", CancellationToken.None);
        var write = await _host.Descriptions.SetAsync(outsider, "WEB", "改", CancellationToken.None);
        var missing = await _host.Descriptions.GetAsync(scenario.Admin, "NOPE", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
            Assert.That(write.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
            Assert.That(missing.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
        }
    }

    [Test]
    public async Task SetAsync_OrganizationAdminWhoIsNotAMember_Succeeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var otherAdmin = new Actor(scenario.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);

        // Act
        var result = await _host.Descriptions.SetAsync(otherAdmin, "WEB", "管理員寫的", CancellationToken.None);

        // Assert
        Assert.That(result.Value.UpdatedBy, Is.EqualTo(otherAdmin.PersonId));
    }

    [Test]
    public async Task SetAsync_ArchivedProject_StillWorks()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var result = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "換主機了", CancellationToken.None);

        // Assert
        Assert.That(result.Value.Text, Is.EqualTo("換主機了"));
    }

    [Test]
    public async Task GetAsync_AnotherOrganization_CannotSeeIt()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "秘密", CancellationToken.None);
        var other = new Scenario(_host);
        await other.CreateProjectAsync("WEB");

        // Act
        var foreign = await _host.Descriptions.GetAsync(other.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(foreign.Value, Is.EqualTo(ProjectDescription.Empty));
    }

    [Test]
    public async Task SetAsync_TooLong_FailsWithTextPath()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var atLimit = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", new string('字', ProjectDescriptionRules.MaxTextLength), CancellationToken.None);
        var over = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", new string('字', ProjectDescriptionRules.MaxTextLength + 1), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(atLimit.Succeeded, Is.True);
            Assert.That((over.Failure?.Kind, over.Failure?.Code), Is.EqualTo((FailureKind.Invalid, WorkErrorCodes.ProjectDescriptionTooLong)));
            Assert.That(over.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "text" }));
        }
    }

    [TestCase("a\0b")]
    [TestCase("a\u0007b")]
    [TestCase("LONE_SURROGATE")]
    public async Task SetAsync_ForbiddenCharacters_FailsWithInvalid(string text)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // 落單的代理字元放在測試屬性裡會被換成別的字元，所以在這裡才組出來。
        var sent = text == "LONE_SURROGATE" ? "a\uD800b" : text;

        // Act
        var result = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", sent, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Invalid, WorkErrorCodes.ProjectDescriptionInvalid)));
            Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "text" }));
        }
    }

    [TestCase("-----BEGIN PRIVATE KEY-----")]
    [TestCase("-----BEGIN RSA PRIVATE KEY-----")]
    [TestCase("-----BEGIN EC PRIVATE KEY-----")]
    [TestCase("-----BEGIN OPENSSH PRIVATE KEY-----")]
    [TestCase("-----BEGIN ENCRYPTED PRIVATE KEY-----")]
    [TestCase("-----BEGIN DSA PRIVATE KEY-----")]
    [TestCase("-----BEGIN PGP PRIVATE KEY BLOCK-----")]
    [TestCase("PuTTY-User-Key-File-3: ssh-ed25519")]
    [TestCase("PuTTY-User-Key-File-2: ssh-rsa")]
    [TestCase("> -----BEGIN PRIVATE KEY-----")]
    [TestCase("- -----BEGIN RSA PRIVATE KEY-----")]
    [TestCase("`-----BEGIN PRIVATE KEY-----`")]
    [TestCase("**-----BEGIN EC PRIVATE KEY-----**")]
    [TestCase("-----BEGIN PRIVATE KEY-----MIIEvQIBADANBgkqhkiG9w0BAQEFAASC")]
    [TestCase("金鑰：-----BEGIN OPENSSH PRIVATE KEY----- b3BlbnNzaC1rZXk")]
    public async Task SetAsync_PrivateKey_FailsWithSecretEvenInTheMiddleOfText(string line)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", $"## 主機\n- 金鑰：\n{line}\nabc\n", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Invalid, WorkErrorCodes.ProjectDescriptionSecret)));
            Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "text" }));
            Assert.That((await _host.Descriptions.GetAsync(scenario.Admin, "WEB", CancellationToken.None)).Value, Is.EqualTo(ProjectDescription.Empty));
        }
    }

    [TestCase("-----BEGIN PUBLIC KEY-----")]
    [TestCase("-----BEGIN CERTIFICATE-----")]
    [TestCase("說明：私鑰（PRIVATE KEY）不要貼在這裡")]
    [TestCase("-----BEGIN OPENSSH PUBLIC KEY-----")]
    [TestCase("Private key 不要貼在這裡，PuTTY-User-Key-File 也不要")]
    public async Task SetAsync_PublicMaterialAndTalkAboutKeys_IsAllowed(string line)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", line, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    [Test]
    public async Task ListTemplatesAsync_ReturnsTheBuiltInWebProjectAsDefault()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var templates = await _host.Descriptions.ListTemplatesAsync(scenario.Person(), CancellationToken.None);

        // Assert
        var template = templates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((template.Id, template.Name, template.IsBuiltIn, template.IsDefault), Is.EqualTo(("web_project", "網站案子", true, true)));
            Assert.That(template.Body, Does.StartWith("## 基本資料"));
            Assert.That(template.Body, Does.Contain("帳號密碼：寫密碼管理器裡的項目名稱，不要貼在這裡"));
            Assert.That(template.Body.Length, Is.LessThanOrEqualTo(ProjectDescriptionRules.MaxTextLength));
        }
    }

    [Test]
    public async Task SetAsync_BuiltInTemplateBody_PassesTheSameChecks()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var body = (await _host.Descriptions.ListTemplatesAsync(scenario.Admin, CancellationToken.None)).Single().Body;

        // Act
        var result = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", body, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Text, Is.EqualTo(body));
    }
}
