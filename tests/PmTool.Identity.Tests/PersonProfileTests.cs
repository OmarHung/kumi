using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>人員的職稱與負責範圍：誰能改、輸入規則、讀取帶出。</summary>
[TestFixture]
public sealed class PersonProfileTests
{
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;

    private IPersonProfileService Profiles => _host.Services.GetRequiredService<IPersonProfileService>();

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await IdentityTestHost.StartAsync();
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateProfile_ByAdmin_SetsBothFieldsAndTheDirectoryReadsThemBack()
    {
        // Arrange
        var admin = await _host.AddUserAsync(_owner.ToActor(), OrganizationRole.Admin);
        var target = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await Profiles.UpdateProfileAsync(
            admin.ToActor(), target.PersonId, new PersonProfile("前端工程師", "WordPress 維運、範例客戶的網站"), CancellationToken.None);

        // Assert
        var listed = (await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None)).Single(p => p.PersonId == target.PersonId);
        var found = (await _host.People.FindManyAsync(_owner.OrganizationId, [target.PersonId], CancellationToken.None))[target.PersonId];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
            Assert.That(result.Value.JobTitle, Is.EqualTo("前端工程師"));
            Assert.That(result.Value.Responsibilities, Is.EqualTo("WordPress 維運、範例客戶的網站"));
            Assert.That(result.Value.Email, Is.EqualTo(target.Email));
            Assert.That(listed, Is.EqualTo(result.Value));
            Assert.That(found, Is.EqualTo(result.Value));
        }
    }

    [Test]
    public async Task UpdateProfile_OnTheirOwnOrAnOwner_IsAllowed()
    {
        // Arrange
        var admin = await _host.AddUserAsync(_owner.ToActor(), OrganizationRole.Admin);

        // Act
        var self = await Profiles.UpdateProfileAsync(admin.ToActor(), admin.PersonId, new PersonProfile("專案經理", null), CancellationToken.None);
        var owner = await Profiles.UpdateProfileAsync(admin.ToActor(), _owner.PersonId, new PersonProfile("負責人", null), CancellationToken.None);

        // Assert
        Assert.That(self.Succeeded, Is.True, self.Failure?.Message);
        Assert.That(owner.Succeeded, Is.True, owner.Failure?.Message);
    }

    [Test]
    public async Task UpdateProfile_TrimsAndTurnsBlankIntoNull()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await Profiles.UpdateProfileAsync(
            _owner.ToActor(), target.PersonId, new PersonProfile("  後端工程師  ", "   "), CancellationToken.None);

        // Assert
        Assert.That(result.Value.JobTitle, Is.EqualTo("後端工程師"));
        Assert.That(result.Value.Responsibilities, Is.Null);
    }

    [Test]
    public async Task UpdateProfile_WithNulls_ClearsBothFields()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, new PersonProfile("設計師", "品牌"), CancellationToken.None);

        // Act
        var result = await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, new PersonProfile(null, null), CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new PersonSummary(target.PersonId, target.DisplayName, target.Email, OrganizationRole.Member, IsDisabled: false)));
    }

    [Test]
    public async Task UpdateProfile_SameValues_SucceedsAgain()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        var profile = new PersonProfile("測試工程師", null);
        await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, profile, CancellationToken.None);

        // Act
        var again = await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, profile, CancellationToken.None);

        // Assert
        Assert.That(again.Succeeded, Is.True);
        Assert.That(again.Value.JobTitle, Is.EqualTo("測試工程師"));
    }

    [Test]
    public async Task UpdateProfile_DisabledTarget_IsAllowed()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, CancellationToken.None);

        // Act
        var result = await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, new PersonProfile("已離職", null), CancellationToken.None);

        // Assert
        Assert.That(result.Value.IsDisabled, Is.True);
        Assert.That(result.Value.JobTitle, Is.EqualTo("已離職"));
    }

    [TestCase(OrganizationRole.Member)]
    [TestCase(OrganizationRole.Guest)]
    public async Task UpdateProfile_ByNonAdmin_ReturnsForbiddenAndChangesNothing(OrganizationRole role)
    {
        // Arrange
        var actor = await _host.AddUserAsync(_owner.ToActor(), role);
        var target = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await Profiles.UpdateProfileAsync(actor.ToActor(), target.PersonId, new PersonProfile("駭客", null), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Forbidden, IdentityErrorCodes.Forbidden);
        var listed = (await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None)).Single(p => p.PersonId == target.PersonId);
        Assert.That(listed.JobTitle, Is.Null);
    }

    [Test]
    public async Task UpdateProfile_ByAnAdminWhoseAccountWasJustDisabled_ReturnsForbidden()
    {
        // Arrange
        var admin = await _host.AddUserAsync(_owner.ToActor(), OrganizationRole.Admin);
        var target = await _host.AddUserAsync(_owner.ToActor());
        await _host.DisableAccountAsync(admin.AccountId);

        // Act
        var result = await Profiles.UpdateProfileAsync(admin.ToActor(), target.PersonId, new PersonProfile("工程師", null), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Forbidden, IdentityErrorCodes.Forbidden);
    }

    [Test]
    public async Task UpdateProfile_UnknownPersonOrAnotherOrganizationsPerson_ReturnsNotFound()
    {
        // Arrange
        var otherOrganization = await _host.AddOrganizationAsync("別的範例組織");
        var theirs = await _host.People.CreateAsync(
            otherOrganization, new NewPerson("別的組織的範例使用者", TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member),
            CancellationToken.None);

        // Act
        var unknown = await Profiles.UpdateProfileAsync(_owner.ToActor(), Guid.CreateVersion7(), new PersonProfile("工程師", null), CancellationToken.None);
        var elsewhere = await Profiles.UpdateProfileAsync(_owner.ToActor(), theirs.Value.PersonId, new PersonProfile("工程師", null), CancellationToken.None);

        // Assert
        AssertFailure(unknown, FailureKind.NotFound, IdentityErrorCodes.NotFound);
        AssertFailure(elsewhere, FailureKind.NotFound, IdentityErrorCodes.NotFound);
    }

    [Test]
    public async Task UpdateProfile_TooLong_ReturnsInvalidWithEachFieldsPath()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        var profile = new PersonProfile(
            new string('字', IdentityRules.MaxJobTitleLength + 1), new string('字', IdentityRules.MaxResponsibilitiesLength + 1));

        // Act
        var result = await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, profile, CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Invalid, IdentityErrorCodes.ProfileInvalid);
        Assert.That(
            result.Failure!.Details!.Select(d => $"{d.Code}|{d.Path}"),
            Is.EqualTo(new[] { $"{IdentityErrorCodes.ProfileInvalid}|jobTitle", $"{IdentityErrorCodes.ProfileInvalid}|responsibilities" }));
    }

    [Test]
    public async Task UpdateProfile_AtTheLimit_Succeeds()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        var profile = new PersonProfile(
            new string('字', IdentityRules.MaxJobTitleLength), new string('字', IdentityRules.MaxResponsibilitiesLength));

        // Act
        var result = await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, profile, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [TestCase("第一行\n第二行", TestName = "UpdateProfile_Newline_ReturnsInvalid")]
    [TestCase("含\0的字", TestName = "UpdateProfile_Nul_ReturnsInvalid")]
    [TestCase("含\tTab", TestName = "UpdateProfile_Tab_ReturnsInvalid")]
    [TestCase("零寬​字元", TestName = "UpdateProfile_ZeroWidth_ReturnsInvalid")]
    [TestCase("雙向‮控制", TestName = "UpdateProfile_BidiControl_ReturnsInvalid")]
    public async Task UpdateProfile_ForbiddenCharacters_ReturnsInvalidForThatField(string text)
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var asTitle = await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, new PersonProfile(text, null), CancellationToken.None);
        var asScope = await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, new PersonProfile(null, text), CancellationToken.None);

        // Assert
        AssertFailure(asTitle, FailureKind.Invalid, IdentityErrorCodes.ProfileInvalid);
        AssertFailure(asScope, FailureKind.Invalid, IdentityErrorCodes.ProfileInvalid);
        Assert.That(asTitle.Failure!.Details!.Single().Path, Is.EqualTo("jobTitle"));
        Assert.That(asScope.Failure!.Details!.Single().Path, Is.EqualTo("responsibilities"));
    }

    [Test]
    public async Task UpdateProfile_InvalidInput_LeavesTheStoredProfileAlone()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, new PersonProfile("設計師", "品牌"), CancellationToken.None);

        // Act
        await Profiles.UpdateProfileAsync(_owner.ToActor(), target.PersonId, new PersonProfile("好", "壞\n壞"), CancellationToken.None);

        // Assert
        var listed = (await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None)).Single(p => p.PersonId == target.PersonId);
        Assert.That(listed.JobTitle, Is.EqualTo("設計師"));
        Assert.That(listed.Responsibilities, Is.EqualTo("品牌"));
    }

    private static void AssertFailure(Result<PersonSummary> result, FailureKind kind, string code)
    {
        Assert.That(result.Failure, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(kind));
            Assert.That(result.Failure.Code, Is.EqualTo(code));
            Assert.That(result.Failure.Message, Is.Not.Empty);
        }
    }
}
