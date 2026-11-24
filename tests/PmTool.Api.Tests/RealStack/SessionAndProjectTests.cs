using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>啟動、用設定建立的擁有者登入、從兩個內建模板開專案、登出。</summary>
[TestFixture]
public sealed class SessionAndProjectTests : RealStackTestBase
{
    [Test]
    public async Task Startup_OwnerFromTheSettings_CanSignInOnceTheServiceIsReady()
    {
        // Arrange
        using var client = Factory.CreateApiClient();

        // Act
        using var ready = await client.GetAsync("/health/ready");
        var user = await RealStackSteps.SignInAsync(client, RealStackFactory.OwnerEmail, RealStackFactory.OwnerPassword);
        var session = await RealStackSteps.GetAsync(client, Sessions.Route);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await ready.Content.ReadAsStringAsync(), Is.EqualTo("""{"status":"ok"}"""));
            Assert.That(user["role"]?.GetValue<string>(), Is.EqualTo("owner"));
            Assert.That(user["email"]?.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerEmail));
            Assert.That(user["displayName"]?.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerName));
            Assert.That(user["organizationName"]?.GetValue<string>(), Is.EqualTo(RealStackFactory.OrganizationName));
            Assert.That(JsonNode.DeepEquals(session["user"], user), Is.True, session.ToJsonString());
        }
    }

    [Test]
    public async Task Projects_FromBothTemplates_AreCreatedAndListed()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var client = owner.Client;

        // Act
        var templates = await RealStackSteps.GetAsync(client, "/v1/templates");
        var simple = await RealStackSteps.CreateProjectAsync(client, "TASKS", "simple_tasks");
        var agency = await RealStackSteps.CreateProjectAsync(client, "CLIENT", "agency_project");
        using var duplicate = await client.PostAsync(
            "/v1/projects", ApiRequests.Json(new { key = "TASKS", name = "Example 重複", template = "simple_tasks" }));
        var list = await RealStackSteps.GetAsync(client, "/v1/projects");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(duplicate, HttpStatusCode.Conflict, WorkErrorCodes.ProjectKeyTaken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KeysOf(templates), Is.SupersetOf(new[] { "simple_tasks", "agency_project" }));
            Assert.That(simple["key"]?.GetValue<string>(), Is.EqualTo("TASKS"));
            Assert.That(simple["myRole"]?.GetValue<string>(), Is.EqualTo("admin"), "開專案的人成為專案的 admin");
            Assert.That(simple["blueprintVersion"]?.GetValue<long>(), Is.EqualTo(1));
            Assert.That(agency["key"]?.GetValue<string>(), Is.EqualTo("CLIENT"));
            Assert.That(RealStackSteps.KeysOf(list), Is.SupersetOf(new[] { "TASKS", "CLIENT" }));
        }
    }

    [Test]
    public async Task ProjectDetail_FromTheAgencyTemplate_HasTheBlueprintMembersAndPermissions()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var client = owner.Client;
        await RealStackSteps.CreateProjectAsync(client, "DETAIL", "agency_project");

        // Act
        var detail = await RealStackSteps.GetAsync(client, "/v1/projects/DETAIL");
        var blueprint = detail["blueprint"];
        var delivery = blueprint?["workflows"]?.AsArray().Single(w => w?["key"]?.GetValue<string>() == "delivery");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detail["project"]?["myRole"]?.GetValue<string>(), Is.EqualTo("admin"));
            Assert.That(blueprint?["metadata"]?["key"]?.GetValue<string>(), Is.EqualTo("detail"), "模板的 metadata 換成這個專案的");
            Assert.That(RealStackSteps.KeysOf(blueprint?["itemTypes"]), Is.EqualTo(new[] { "requirement", "change_request", "task", "subtask" }));
            Assert.That(RealStackSteps.KeysOf(blueprint?["fields"]), Does.Contain("client_signoff_date"));
            Assert.That(RealStackSteps.KeysOf(delivery?["states"]), Is.EqualTo(new[]
            {
                "confirming", "quoting", "designing", "developing", "internal_qa", "client_qa", "closed",
            }));
            Assert.That(detail["myPermissions"]?.AsArray().Select(p => p?.GetValue<string>()), Does.Contain(WorkPermissions.ProjectManage));
            Assert.That(JsonNode.DeepEquals(detail["members"], new JsonArray(new JsonObject
            {
                ["personId"] = owner.PersonId,
                ["displayName"] = RealStackFactory.OwnerName,
                ["role"] = "admin",
            })), Is.True, detail["members"]?.ToJsonString());
        }
    }

    [Test]
    public async Task SignOut_AfterSignIn_EndsTheSession()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var client = owner.Client;

        // Act
        using var signOut = await client.DeleteAsync(Sessions.Route);
        using var afterwards = await client.GetAsync(Sessions.Route);

        // Assert
        Assert.That(signOut.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await ApiRequests.AssertErrorEnvelopeAsync(afterwards, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }
}
