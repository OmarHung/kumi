using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>新增人員：權限、每一種資料的問題、Email 重複（包含同時新增）。</summary>
[TestFixture]
public sealed class PeopleDirectoryCreateTests
{
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await IdentityTestHost.StartAsync();
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_ByOwner_CreatesPersonWhoCanSignIn()
    {
        // Arrange
        var email = TestData.UniqueEmail();
        var person = new NewPerson("  範例使用者  ", $"  {email.ToUpperInvariant()} ", TestData.Password, OrganizationRole.Member);

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None);
        var user = await _host.Authentication.SignInAsync(email, TestData.Password, CancellationToken.None);

        // Assert：存的是去掉空白的名稱與正規化的 Email
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Value, Is.EqualTo(new PersonSummary(result.Value.PersonId, "範例使用者", email, OrganizationRole.Member, IsDisabled: false)));
        Assert.That(user, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(user!.PersonId, Is.EqualTo(result.Value.PersonId));
            Assert.That(user.OrganizationId, Is.EqualTo(_owner.OrganizationId));
            Assert.That(user.Role, Is.EqualTo(OrganizationRole.Member));
            Assert.That(user.IsOrganizationAdmin, Is.False);
        }
    }

    [TestCase(OrganizationRole.Member)]
    [TestCase(OrganizationRole.Guest)]
    public async Task CreateAsync_ByNonAdmin_ReturnsForbidden(OrganizationRole actorRole)
    {
        // Arrange
        var actor = await CreateAndSignInAsync(actorRole);
        var person = new NewPerson("範例使用者", TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member);

        // Act
        var result = await _host.People.CreateAsync(actor.ToActor(), person, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.Forbidden));
        Assert.That(await _host.Authentication.SignInAsync(person.Email, person.Password, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task CreateAsync_AdminAddsAdmin_Succeeds()
    {
        // Arrange
        var admin = await CreateAndSignInAsync(OrganizationRole.Admin);
        var person = new NewPerson("範例管理員", TestData.UniqueEmail(), TestData.Password, OrganizationRole.Admin);

        // Act
        var result = await _host.People.CreateAsync(admin.ToActor(), person, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Value.Role, Is.EqualTo(OrganizationRole.Admin));
    }

    [Test]
    public async Task CreateAsync_AdminAddsOwner_ReturnsForbidden()
    {
        // Arrange：管理員不能替別人（或替自己的分身）開出比自己大的權限
        var admin = await CreateAndSignInAsync(OrganizationRole.Admin);
        var person = new NewPerson("範例擁有者", TestData.UniqueEmail(), TestData.Password, OrganizationRole.Owner);

        // Act
        var result = await _host.People.CreateAsync(admin.ToActor(), person, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.Forbidden));
    }

    [Test]
    public async Task CreateAsync_OwnerAddsOwner_Succeeds()
    {
        // Arrange
        var person = new NewPerson("範例擁有者", TestData.UniqueEmail(), TestData.Password, OrganizationRole.Owner);

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Value.Role, Is.EqualTo(OrganizationRole.Owner));
    }

    [TestCaseSource(nameof(InvalidPeople))]
    public async Task CreateAsync_InvalidInput_ReturnsTheMatchingCodeAndCreatesNothing(NewPerson person, string code, string path)
    {
        // Arrange
        var peopleBefore = await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None);

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None);

        // Assert
        Assert.That(result.Failure, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure.Code, Is.EqualTo(code));
            Assert.That(result.Failure.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (code, path) }));
        }

        Assert.That(await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None), Is.EqualTo(peopleBefore));
    }

    [TestCaseSource(typeof(TestData), nameof(TestData.ImpossibleEmails))]
    public async Task CreateAsync_EmailThatCannotBeValid_ReturnsEmailInvalid(string email)
    {
        // Arrange
        var person = new NewPerson("範例使用者", email, TestData.Password, OrganizationRole.Member);

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
        Assert.That(result.Failure?.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (IdentityErrorCodes.EmailInvalid, "email") }));
    }

    [Test]
    public async Task CreateAsync_DecomposedEmail_StoresTheComposedForm()
    {
        // Arrange
        var (composed, decomposed) = TestData.UniqueAccentedEmail();
        var person = new NewPerson("範例使用者", decomposed, TestData.Password, OrganizationRole.Member);

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Email, Is.EqualTo(composed));
        Assert.That(await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None),
            Has.One.Matches<PersonSummary>(p => p.PersonId == result.Value.PersonId && p.Email == composed));
    }

    [Test]
    public async Task CreateAsync_EmailUsedInTheOtherUnicodeForm_ReturnsEmailTaken()
    {
        // Arrange：兩種寫法畫面上一模一樣，建成兩個帳號就分不出誰是誰
        var (composed, decomposed) = TestData.UniqueAccentedEmail();
        await _host.People.CreateAsync(_owner.ToActor(), new NewPerson("範例甲", composed, TestData.Password, OrganizationRole.Member), CancellationToken.None);
        var duplicate = new NewPerson("範例乙", decomposed, "another-password", OrganizationRole.Member);

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), duplicate, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.EmailTaken));
    }

    [TestCaseSource(nameof(BoundaryPeople))]
    public async Task CreateAsync_ValuesAtTheLimits_Succeeds(NewPerson person)
    {
        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [Test]
    public async Task CreateAsync_SeveralProblems_ListsEachOneInDetails()
    {
        // Arrange
        var person = new NewPerson(" ", "not-an-email", "short", OrganizationRole.Member);

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None);

        // Assert：網頁一次把三個欄位的問題都標出來
        Assert.That(result.Failure?.Details.Select(d => d.Code), Is.EqualTo(new[]
        {
            IdentityErrorCodes.NameRequired, IdentityErrorCodes.EmailInvalid, IdentityErrorCodes.PasswordTooShort,
        }));
        Assert.That(result.Failure!.Code, Is.EqualTo(IdentityErrorCodes.NameRequired));
        Assert.That(result.Failure.Details.Select(d => d.Message), Has.All.Not.Empty);
    }

    [Test]
    public async Task CreateAsync_EmailUsedInAnotherCase_ReturnsEmailTaken()
    {
        // Arrange
        var email = TestData.UniqueEmail();
        await _host.People.CreateAsync(_owner.ToActor(), new NewPerson("範例甲", email, TestData.Password, OrganizationRole.Member), CancellationToken.None);
        var duplicate = new NewPerson("範例乙", $" {email.ToUpperInvariant()}", "another-password", OrganizationRole.Member);

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), duplicate, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.EmailTaken));
        Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "email" }));
        Assert.That(await _host.Authentication.SignInAsync(email, "another-password", CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task CreateAsync_SameEmailAtTheSameTime_OnlyOneSucceeds()
    {
        // Arrange
        var email = TestData.UniqueEmail();
        var people = Enumerable.Range(0, 5)
            .Select(i => new NewPerson($"範例使用者 {i}", email, TestData.Password, OrganizationRole.Member))
            .ToList();

        // Act
        var results = await Task.WhenAll(people.Select(person =>
            Task.Run(() => _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None))));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
        Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Has.All.EqualTo(IdentityErrorCodes.EmailTaken));
    }

    [Test]
    public void CreateAsync_UndefinedRole_Throws()
    {
        // Arrange
        var person = new NewPerson("範例使用者", TestData.UniqueEmail(), TestData.Password, (OrganizationRole)99);

        // Act & Assert：API 只會傳認得的角色，走到這裡是程式寫錯了
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None));
    }

    private static IEnumerable<TestCaseData> InvalidPeople()
    {
        var valid = new NewPerson("範例使用者", "valid@example.com", TestData.Password, OrganizationRole.Member);
        string[] badEmails =
        [
            "", "not-an-email", "@example.com", "user@", "user@@example.com", "user@example", "user name@example.com",
            "user@.example.com", "user@example.com.", "user@example..com", new string('a', IdentityRules.MaxEmailLength - 11) + "@example.com",
        ];

        yield return Case(valid with { DisplayName = "" }, IdentityErrorCodes.NameRequired, "displayName", "名稱空字串");
        yield return Case(valid with { DisplayName = " \t " }, IdentityErrorCodes.NameRequired, "displayName", "名稱只有空白");
        yield return Case(
            valid with { DisplayName = new string('名', IdentityRules.MaxDisplayNameLength + 1) },
            IdentityErrorCodes.NameTooLong, "displayName", "名稱太長");

        // 合約沒有「名稱有不能用的字元」的代碼，沿用工作引擎的做法歸在 name_required：這種名稱等於沒有可用的名稱。
        yield return Case(valid with { DisplayName = "範例" + (char)0x0000 + "使用者" }, IdentityErrorCodes.NameRequired, "displayName", "名稱有 NUL");
        yield return Case(valid with { DisplayName = "範例" + (char)0x000A + "使用者" }, IdentityErrorCodes.NameRequired, "displayName", "名稱中間有換行");
        yield return Case(valid with { DisplayName = "範例" + (char)0xDC00 + "使用者" }, IdentityErrorCodes.NameRequired, "displayName", "名稱有落單的代理字元");
        foreach (var email in badEmails)
        {
            yield return Case(valid with { Email = email }, IdentityErrorCodes.EmailInvalid, "email", $"Email「{email[..Math.Min(email.Length, 24)]}」");
        }

        yield return Case(
            valid with { Password = new string('p', IdentityRules.MinPasswordLength - 1) },
            IdentityErrorCodes.PasswordTooShort, "password", "密碼太短");
        yield return Case(
            valid with { Password = new string('p', IdentityRules.MaxPasswordLength + 1) },
            IdentityErrorCodes.PasswordTooLong, "password", "密碼太長");
    }

    private static IEnumerable<TestCaseData> BoundaryPeople()
    {
        yield return new TestCaseData(new NewPerson(
            new string('名', IdentityRules.MaxDisplayNameLength), TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member))
            .SetArgDisplayNames("名稱剛好到上限");
        yield return new TestCaseData(new NewPerson(
            "範例使用者" + char.ConvertFromUtf32(0x1F600), TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member))
            .SetArgDisplayNames("名稱有表情符號（成對的代理字元）");
        yield return new TestCaseData(new NewPerson(
            "範例使用者", TestData.UniqueAccentedEmail().Composed, TestData.Password, OrganizationRole.Member))
            .SetArgDisplayNames("Email 有重音字母");
        yield return new TestCaseData(new NewPerson(
            "範例使用者", new string('a', IdentityRules.MaxEmailLength - 12) + "@example.com", TestData.Password, OrganizationRole.Member))
            .SetArgDisplayNames("Email 剛好到上限");
        yield return new TestCaseData(new NewPerson(
            "範例使用者", TestData.UniqueEmail(), new string('p', IdentityRules.MinPasswordLength), OrganizationRole.Guest))
            .SetArgDisplayNames("密碼剛好到下限");
        yield return new TestCaseData(new NewPerson(
            "範例使用者", TestData.UniqueEmail(), new string('p', IdentityRules.MaxPasswordLength), OrganizationRole.Admin))
            .SetArgDisplayNames("密碼剛好到上限");
    }

    private static TestCaseData Case(NewPerson person, string code, string path, string name) =>
        new TestCaseData(person, code, path).SetArgDisplayNames(name);

    private async Task<AuthenticatedUser> CreateAndSignInAsync(OrganizationRole role)
    {
        var person = new NewPerson("範例使用者", TestData.UniqueEmail(), TestData.Password, role);
        var created = await _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, created.Failure?.Message);
        return await _host.Authentication.SignInAsync(person.Email, person.Password, CancellationToken.None)
            ?? throw new InvalidOperationException("剛建立的人登入不了。");
    }
}
