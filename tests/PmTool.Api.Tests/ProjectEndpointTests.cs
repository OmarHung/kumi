using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint.Model;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class ProjectEndpointTests
{
    [Test]
    public async Task Templates_SignedIn_ReturnsKeyNameDescriptionAndPreview()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.Templates =
        [
            new TemplateSummary("simple_tasks", "簡單任務", null, [], [], [], 0),
            new TemplateSummary(
                "agency_project",
                "接案專案",
                "設計、開發、驗收",
                [new TemplateWorkflowPreview("交付流程", ["需求", "需求變更單"], ["需求確認", "報價", "結案"], ["撤回"])
                {
                    StateCategories = [StateCategory.NotStarted, StateCategory.InProgress, StateCategory.Done],
                }],
                [new TemplateParentPreview("需求變更單", ["需求"])],
                ["報價金額", "客戶驗收日"],
                5),
        ];
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/templates");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            """
            [{"key":"simple_tasks","name":"簡單任務","description":null,"workflows":[],"parents":[],"fields":[],"exampleCount":0},
             {"key":"agency_project","name":"接案專案","description":"設計、開發、驗收",
              "workflows":[{"name":"交付流程","itemTypes":["需求","需求變更單"],"states":["需求確認","報價","結案"],"stateCategories":["not_started","in_progress","done"],"canceledStates":["撤回"]}],
              "parents":[{"itemType":"需求變更單","parents":["需求"]}],
              "fields":["報價金額","客戶驗收日"],"exampleCount":5}]
            """);
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task ListProjects_SignedIn_PassesTheSessionAsActorAndReturnsTheContractShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnList = _ => Task.FromResult<IReadOnlyList<ProjectSummary>>([TestData.Project("WEB", null)]);
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync("/v1/projects");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.Projects.ListCalls, Is.EqualTo(new[] { TestUsers.Owner.ToActor() }));
            Assert.That(data?[0]?["id"]?.GetValue<Guid>(), Is.EqualTo(TestData.Project().Id));
            Assert.That(data?[0]?["key"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(data?[0]?["name"]?.GetValue<string>(), Is.EqualTo("Example 網站"));
            Assert.That(data?[0]?["blueprintVersion"]?.GetValue<long>(), Is.EqualTo(1));
            Assert.That(data?[0]?.AsObject().ContainsKey("myRole") == true && data[0]?["myRole"] is null, Is.True);
            Assert.That(data?[0]?["createdAt"]?.GetValue<string>(), Is.EqualTo("2026-11-13T01:02:03Z"));
        }
    }

    [Test]
    public async Task CreateProject_Accepted_Returns201WithLocationAndPassesTheRequest()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects", ApiRequests.Json(new { key = "WEB", name = "Example 網站", template = "simple_tasks" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/v1/projects/WEB"));
            Assert.That(data?["key"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(factory.Modules.Projects.CreateCalls, Is.EqualTo(new[]
            {
                (TestUsers.Owner.ToActor(), new NewProject("WEB", "Example 網站", "simple_tasks")),
            }));
        }
    }

    [Test]
    public async Task CreateProject_Description_IsPassedToTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects",
            ApiRequests.Json(new { key = "WEB", name = "Example 網站", template = "simple_tasks", description = "## 網址" }));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        Assert.That(factory.Modules.Projects.CreateCalls.Single().Project.Description, Is.EqualTo("## 網址"));
    }

    [Test]
    public async Task CreateProject_InvalidDescription_Returns422WithTheDescriptionPath()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnCreate = (_, _) => new Failure(
            FailureKind.Invalid, WorkErrorCodes.ProjectDescriptionSecret, "失敗")
        {
            Details = [new FailureDetail(WorkErrorCodes.ProjectDescriptionSecret, "description", "失敗")],
        };
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects", ApiRequests.Json(new { key = "WEB", name = "Example", template = "simple_tasks", description = "x" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ProjectDescriptionSecret);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("description"));
    }

    [Test]
    public async Task CreateProject_IncludeExamples_IsPassedToTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects",
            ApiRequests.Json(new { key = "WEB", name = "Example 網站", template = "simple_tasks", includeExamples = true }));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        Assert.That(factory.Modules.Projects.CreateCalls, Is.EqualTo(new[]
        {
            (TestUsers.Owner.ToActor(), new NewProject("WEB", "Example 網站", "simple_tasks", IncludeExamples: true)),
        }));
    }

    [Test]
    public async Task CreateProject_IncludeExamplesIsNotABoolean_Returns400AndDoesNotCallTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects",
            ApiRequests.Json(new { key = "WEB", name = "Example 網站", template = "simple_tasks", includeExamples = "yes" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("includeExamples"));
            Assert.That(factory.Modules.Projects.CreateCalls, Is.Empty);
        }
    }

    [TestCase(FailureKind.Invalid, HttpStatusCode.UnprocessableEntity)]
    [TestCase(FailureKind.NotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Conflict, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Forbidden, HttpStatusCode.Forbidden)]
    public async Task CreateProject_ModuleFails_ReturnsTheMatchingStatusWithTheModuleCodeAndDetails(
        FailureKind kind, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnCreate = (_, _) => new Failure(kind, "project.example_failure", "模組說的原因。")
        {
            Details = [new FailureDetail("project.key_invalid", "key", "代號的格式不對。")],
        };
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects", ApiRequests.Json(new { key = "web", name = "Example", template = "simple_tasks" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, expected, "project.example_failure");
        var envelope = JsonNode.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(envelope?["error"]?["message"]?.GetValue<string>(), Is.EqualTo("模組說的原因。"));
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "project.key_invalid|key" }));
            Assert.That(details[0]?["message"]?.GetValue<string>(), Is.EqualTo("代號的格式不對。"));
        }
    }

    [Test]
    public async Task CreateProject_ModuleFailsWithAnUnknownKind_Returns500()
    {
        // Arrange：合約以後加了新的種類卻沒有對應時，不能默默回 200 或亂猜一個狀態碼。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnCreate = (_, _) => new Failure((FailureKind)99, "project.something_new", "新的失敗。");
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects", ApiRequests.Json(new { key = "WEB", name = "Example", template = "simple_tasks" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.InternalServerError, "project.something_new");
    }

    [Test]
    public async Task GetProject_Found_ReturnsProjectBlueprintMembersWithNamesAndPermissions()
    {
        // Arrange：成員裡有一個已經不在組織的人，名稱要給替代文字，不能讓整個回應失敗。
        var departed = Guid.Parse("0190a000-0000-7000-8000-0000000000d9");
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnGet = (_, key) => new ProjectDetail(
            TestData.Project(key),
            TestData.Blueprint(),
            [new ProjectMember(TestUsers.Member.PersonId, "editor"), new ProjectMember(departed, "viewer")],
            [WorkPermissions.ItemRead, WorkPermissions.ItemUpdate]);
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.Projects.GetCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB") }));
            Assert.That(data?["project"]?["key"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(data?["blueprint"]?["apiVersion"]?.GetValue<string>(), Is.EqualTo("blueprint/v0"));
            Assert.That(data?["blueprint"]?["workflows"]?[0]?["states"]?[1]?["category"]?.GetValue<string>(), Is.EqualTo("in_progress"));
            Assert.That(JsonNode.DeepEquals(data?["members"], new JsonArray(
                new JsonObject { ["personId"] = TestUsers.Member.PersonId, ["displayName"] = TestUsers.Member.DisplayName, ["role"] = "editor" },
                new JsonObject { ["personId"] = departed, ["displayName"] = "（已不在組織）", ["role"] = "viewer" })), Is.True, data?["members"]?.ToJsonString());
            Assert.That(data?["myPermissions"]?.AsArray().Select(p => p?.GetValue<string>()), Is.EqualTo(new[] { "item.read", "item.update" }));
            Assert.That(factory.Modules.People.FindManyCalls, Has.Count.EqualTo(1), "成員的名稱一次查完");
        }
    }

    [Test]
    public async Task GetProject_ModuleSaysNotFound_Returns404WithTheModuleCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnGet = (_, _) =>
            new Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到這個專案。");
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/NOPE");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    [Test]
    public async Task SetMember_PersonInTheOrganization_ReturnsTheMemberWithDisplayName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);
        var person = TestUsers.Member.PersonId;

        // Act
        using var response = await client.PutAsync($"/v1/projects/WEB/members/{person}", ApiRequests.Json(new { role = "editor" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, new JsonObject
            {
                ["personId"] = person,
                ["displayName"] = TestUsers.Member.DisplayName,
                ["role"] = "editor",
            }), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.Projects.SetMemberCalls, Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), "WEB", person, "editor") }));
            Assert.That(factory.Modules.People.FindManyCalls.Single().OrganizationId, Is.EqualTo(TestUsers.OrganizationId));
        }
    }

    [Test]
    public async Task SetMember_PersonNotInTheCallersOrganization_Returns404WithoutAskingTheProjectService()
    {
        // Arrange：別的組織的人，目錄查不到。
        var outsider = Guid.Parse("0190a000-0000-7000-8000-0000000000e1");
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.People.Add(Guid.Parse("0190a000-0000-7000-8000-0000000000e0"), new PersonSummary(
            outsider, "Outsider Example", "outsider@example.com", OrganizationRole.Member, IsDisabled: false));
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync($"/v1/projects/WEB/members/{outsider}", ApiRequests.Json(new { role = "editor" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.PersonNotFound);
        Assert.That(factory.Modules.Projects.SetMemberCalls, Is.Empty);
    }

    [Test]
    public async Task SetMember_ProjectNotVisibleToTheCaller_Returns404ProjectWithoutLookingUpThePerson()
    {
        // Arrange：先查人的話，看不到專案的人也能拿這個端點分辨一個人員編號存不存在。
        var unknownPerson = Guid.Parse("0190a000-0000-7000-8000-0000000000e2");
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnGet = (_, _) =>
            new Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到這個專案。");
        using var client = await SignedInClientAsync(factory);

        // Act
        using var known = await client.PutAsync(
            $"/v1/projects/WEB/members/{TestUsers.Owner.PersonId}", ApiRequests.Json(new { role = "editor" }));
        using var unknown = await client.PutAsync(
            $"/v1/projects/WEB/members/{unknownPerson}", ApiRequests.Json(new { role = "editor" }));

        // Assert：人存不存在，回的都一樣。
        await ApiRequests.AssertErrorEnvelopeAsync(known, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(unknown, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.People.FindManyCalls, Is.Empty);
            Assert.That(factory.Modules.Projects.SetMemberCalls, Is.Empty);
        }
    }

    [Test]
    public async Task SetMember_CallerWithoutProjectManage_Returns403WithoutLookingUpThePerson()
    {
        // Arrange：看得到專案，但只是檢視者。
        var unknownPerson = Guid.Parse("0190a000-0000-7000-8000-0000000000e3");
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnGet = (_, key) => new ProjectDetail(
            TestData.Project(key, "viewer"), TestData.Blueprint(), [], [WorkPermissions.ItemRead, WorkPermissions.BlueprintRead]);
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync(
            $"/v1/projects/WEB/members/{unknownPerson}", ApiRequests.Json(new { role = "editor" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.Projects.GetCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB") }));
            Assert.That(factory.Modules.People.FindManyCalls, Is.Empty);
            Assert.That(factory.Modules.Projects.SetMemberCalls, Is.Empty);
        }
    }

    [Test]
    public async Task SetMember_ModuleRejectsTheRole_Returns422WithTheModuleCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnSetMember = (_, _, _, _) =>
            new Failure(FailureKind.Invalid, WorkErrorCodes.MemberUnknownRole, "沒有這個角色。");
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(
            $"/v1/projects/WEB/members/{TestUsers.Member.PersonId}", ApiRequests.Json(new { role = "boss" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.MemberUnknownRole);
    }

    [Test]
    public async Task SetMember_PersonIdIsNotAGuid_Returns404()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync("/v1/projects/WEB/members/not-a-guid", ApiRequests.Json(new { role = "editor" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
    }

    internal static async Task<HttpClient> SignedInClientAsync(ApiFactory factory, AuthenticatedUser? user = null)
    {
        var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, user ?? TestUsers.Member);
        return client;
    }
}
