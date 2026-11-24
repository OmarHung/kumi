using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>專案的改名、封存與取消封存：API 只轉換格式與狀態碼，規則由工作引擎判斷。</summary>
[TestFixture]
public sealed class ProjectArchiveEndpointTests
{
    [Test]
    public async Task ListProjects_ArchivedAndLive_WritesArchivedAtWithNullForTheLiveOne()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnList = _ => Task.FromResult<IReadOnlyList<ProjectSummary>>(
            [TestData.Project("WEB"), TestData.Project("OLD", archivedAt: TestData.UpdatedAt)]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?[0]?.AsObject().ContainsKey("archivedAt") == true && data[0]!["archivedAt"] is null, Is.True);
            Assert.That(data?[1]?["archivedAt"]?.GetValue<string>(), Is.Not.Null);
        }
    }

    [Test]
    public async Task GetProject_Archived_WritesArchivedAtInTheProject()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnGet = (_, key) => new ProjectDetail(
            TestData.Project(key, archivedAt: TestData.UpdatedAt), TestData.Blueprint(), [], [WorkPermissions.ItemRead, WorkPermissions.ProjectManage]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["project"]?["archivedAt"]?.GetValue<DateTimeOffset>(), Is.EqualTo(TestData.UpdatedAt));
    }

    [Test]
    public async Task Rename_Accepted_ReturnsTheProjectAndPassesTheNameToTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync("/v1/projects/WEB/name", ApiRequests.Json(new { name = "新名稱" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["key"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(factory.Modules.Projects.RenameCalls, Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), "WEB", "新名稱") }));
        }
    }

    [Test]
    public async Task Rename_InvalidName_Returns422WithTheDetailPathName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnRename = (_, _, _) =>
            new Failure(FailureKind.Invalid, WorkErrorCodes.ProjectNameRequired, "請填專案名稱。")
            {
                Details = [new FailureDetail(WorkErrorCodes.ProjectNameRequired, "name", "請填專案名稱。")],
            };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync("/v1/projects/WEB/name", ApiRequests.Json(new { name = " " }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ProjectNameRequired);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("name"));
    }

    [Test]
    public async Task Archive_Accepted_ReturnsTheArchivedProject()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/archive", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["archivedAt"]?.GetValue<DateTimeOffset>(), Is.EqualTo(TestData.UpdatedAt));
            Assert.That(factory.Modules.Projects.ArchiveCalls, Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), "WEB") }));
        }
    }

    [Test]
    public async Task Unarchive_Accepted_ReturnsTheProjectWithNullArchivedAt()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/unarchive", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?.AsObject().ContainsKey("archivedAt") == true && data["archivedAt"] is null, Is.True);
            Assert.That(factory.Modules.Projects.UnarchiveCalls, Has.Count.EqualTo(1));
        }
    }

    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, HttpStatusCode.Conflict)]
    public async Task Archive_ModuleFails_MapsTheFailureToTheStatusCode(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnArchive = (_, _) => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/archive", null);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }
}
