using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Knowledge;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 知識庫的端到端（專案空間）：看得到專案的人就看得到，能不能改依專案的角色；專案封存後只能看。
/// 整個類別只登入三次（擁有者、編輯者、檢視者）：登入有限流。
/// </summary>
[TestFixture]
public sealed class KnowledgeProjectSpaceFlowTests : RealStackTestBase
{
    private const string Project = "KBP";

    private SignedInClient _owner = null!;
    private SignedInClient _editor = null!;
    private SignedInClient _viewer = null!;
    private SignedInClient _stranger = null!;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(_owner.Client, Project, "simple_tasks");
        _editor = await AddMemberAsync(_owner, Project, "editor", "Editor Example", RealStackSteps.UniqueEmail("editor"));
        _viewer = await AddMemberAsync(_owner, Project, "viewer", "Viewer Example", RealStackSteps.UniqueEmail("viewer"));

        // 不是專案成員的一般成員：組織空間看得到，專案空間看不到。
        var email = RealStackSteps.UniqueEmail("stranger");
        await RealStackSteps.CreateMemberAsync(_owner.Client, "Stranger Example", email);
        _stranger = await SignInAsAsync(email, RealStackSteps.MemberPassword);
    }

    [OneTimeTearDown]
    public void DisposeClients()
    {
        _owner?.Dispose();
        _editor?.Dispose();
        _viewer?.Dispose();
        _stranger?.Dispose();
    }

    [Test]
    public async Task ProjectSpace_ViewerReadsButCannotWrite_EditorWrites_StrangerSeesNothing_AndArchivingTheProjectMakesItReadOnly()
    {
        // Arrange
        using var created = await _owner.Client.PostAsync(
            "/v1/knowledge/spaces", ApiRequests.Json(new { key = Project, name = "專案文件", projectKey = Project }));
        var space = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var spaceRoute = $"/v1/knowledge/spaces/{Project}";

        // Act
        using var editorCreate = await _editor.Client.PostAsync($"{spaceRoute}/pages", ApiRequests.Json(new { parentNumber = (int?)null, title = "部署說明", body = "內文" }));
        var page = await ApiRequests.AssertSuccessEnvelopeAsync(editorCreate, HttpStatusCode.Created);
        var pageRoute = $"{spaceRoute}/pages/{page!["number"]}";
        var viewerRead = await RealStackSteps.GetAsync(_viewer.Client, pageRoute);
        using var viewerCreate = await _viewer.Client.PostAsync($"{spaceRoute}/pages", ApiRequests.Json(new { parentNumber = (int?)null, title = "不行" }));
        using var viewerPatch = await _viewer.Client.PatchAsync(pageRoute, ApiRequests.Json(new { body = "改", expectedRevision = 1 }));
        using var viewerDelete = await _viewer.Client.DeleteAsync(pageRoute);
        using var editorPatch = await _editor.Client.PatchAsync(pageRoute, ApiRequests.Json(new { body = "新內文", expectedRevision = 1 }));
        var strangerSpaces = await RealStackSteps.GetAsync(_stranger.Client, "/v1/knowledge/spaces");
        using var strangerTree = await _stranger.Client.GetAsync($"{spaceRoute}/tree");
        using var editorCreatesSpace = await _editor.Client.PostAsync(
            "/v1/knowledge/spaces", ApiRequests.Json(new { key = "KBQ", name = "不行", projectKey = Project }));
        using var archivedProject = await _owner.Client.PostAsync($"/v1/projects/{Project}/archive", null);
        await ApiRequests.AssertSuccessEnvelopeAsync(archivedProject, HttpStatusCode.OK);
        using var editorAfterArchive = await _editor.Client.PatchAsync(pageRoute, ApiRequests.Json(new { body = "封存後", expectedRevision = 2 }));
        var readAfterArchive = await RealStackSteps.GetAsync(_editor.Client, pageRoute);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(space?["project"]?["key"]?.GetValue<string>(), Is.EqualTo(Project));
            Assert.That(space?["project"]?.AsObject().Select(p => p.Key), Is.EquivalentTo(new[] { "key", "name", "isArchived" }));
            Assert.That(viewerRead["body"]?.GetValue<string>(), Is.EqualTo("內文"));
            Assert.That(viewerRead["myPermissions"]?["canEdit"]?.GetValue<bool>(), Is.False);
            Assert.That(editorPatch.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(strangerSpaces.AsArray().Select(s => s!["key"]!.GetValue<string>()), Does.Not.Contain(Project));
            Assert.That(readAfterArchive["body"]?.GetValue<string>(), Is.EqualTo("新內文"));
            Assert.That(readAfterArchive["space"]?["project"]?["isArchived"]?.GetValue<bool>(), Is.True);
            Assert.That(readAfterArchive["myPermissions"]?["canEdit"]?.GetValue<bool>(), Is.False);
        }

        await ApiRequests.AssertErrorEnvelopeAsync(viewerCreate, HttpStatusCode.Forbidden, KnowledgeErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(viewerPatch, HttpStatusCode.Forbidden, KnowledgeErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(viewerDelete, HttpStatusCode.Forbidden, KnowledgeErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(strangerTree, HttpStatusCode.NotFound, KnowledgeErrorCodes.SpaceNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(editorCreatesSpace, HttpStatusCode.Forbidden, KnowledgeErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(editorAfterArchive, HttpStatusCode.Conflict, KnowledgeErrorCodes.SpaceArchived);
    }
}
