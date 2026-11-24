using System.Net;
using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>PATCH 要分得出三種情況：屬性沒出現（不改）、給了 null（清掉）、給了值（改成它）。</summary>
[TestFixture]
public sealed class ItemPatchTests
{
    private const string Route = "/v1/items/WEB-1";

    [Test]
    public async Task Patch_OnlyTitle_LeavesEveryOtherPropertyUnset()
    {
        // Arrange & Act
        var (response, patch) = await PatchAsync("""{"title":"新標題"}""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(patch.Title.HasValue, Is.True);
            Assert.That(patch.Title.Value, Is.EqualTo("新標題"));
            Assert.That(patch.AssigneeId.HasValue, Is.False);
            Assert.That(patch.Priority.HasValue, Is.False);
            Assert.That(patch.StartDate.HasValue, Is.False);
            Assert.That(patch.DueDate.HasValue, Is.False);
            Assert.That(patch.Estimate.HasValue, Is.False);
            Assert.That(patch.ParentKey.HasValue, Is.False);
            Assert.That(patch.Fields, Is.Empty);
        }
    }

    [Test]
    public async Task Patch_Nulls_AreRequestsToClear()
    {
        // Arrange & Act
        var (response, patch) = await PatchAsync(
            """{"assigneeId":null,"startDate":null,"dueDate":null,"estimate":null,"parentKey":null,"fields":{"impact":null}}""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(patch.AssigneeId, Is.EqualTo(new Optional<Guid?>(null)));
            Assert.That(patch.StartDate, Is.EqualTo(new Optional<DateOnly?>(null)));
            Assert.That(patch.DueDate, Is.EqualTo(new Optional<DateOnly?>(null)));
            Assert.That(patch.Estimate, Is.EqualTo(new Optional<decimal?>(null)));
            Assert.That(patch.ParentKey, Is.EqualTo(new Optional<string?>(null)));
            Assert.That(patch.Title.HasValue, Is.False);
            Assert.That(patch.Fields["impact"].ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
    }

    [Test]
    public async Task Patch_Values_ArePassedThrough()
    {
        // Arrange & Act
        var (response, patch) = await PatchAsync($$$"""
            {"assigneeId":"{{{TestUsers.Owner.PersonId}}}","priority":"low","startDate":"2026-12-01","dueDate":"2026-12-24",
             "estimate":8,"parentKey":"WEB-9","fields":{"impact":"medium","tags":["a","b"]}}
            """);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(patch.AssigneeId.Value, Is.EqualTo(TestUsers.Owner.PersonId));
            Assert.That(patch.Priority.Value, Is.EqualTo(Priority.Low));
            Assert.That(patch.StartDate.Value, Is.EqualTo(new DateOnly(2026, 12, 1)));
            Assert.That(patch.DueDate.Value, Is.EqualTo(new DateOnly(2026, 12, 24)));
            Assert.That(patch.Estimate.Value, Is.EqualTo(8m));
            Assert.That(patch.ParentKey.Value, Is.EqualTo("WEB-9"));
            Assert.That(patch.Fields["impact"].GetString(), Is.EqualTo("medium"));
            Assert.That(patch.Fields["tags"].GetArrayLength(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Patch_EmptyObject_PassesAnEmptyPatch()
    {
        // Arrange & Act
        var (response, patch) = await PatchAsync("{}");

        // Assert
        Assert.That(response, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(patch, Is.EqualTo(new WorkItemPatch { Fields = patch.Fields }));
    }

    [Test]
    public async Task Patch_PriorityNull_IsTreatedAsNone()
    {
        // Arrange & Act：新增項目也這樣收，兩邊一致。
        var (response, patch) = await PatchAsync("""{"priority":null}""");

        // Assert
        Assert.That(response, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(patch.Priority, Is.EqualTo(new Optional<Priority>(Priority.None)));
    }

    [Test]
    public async Task Patch_LongTextAtTheModuleLimitInChinese_ReachesTheModule()
    {
        // Arrange & Act：一個字 3 個位元組，五萬字就超過 64 KiB。
        var notes = new string('長', WorkRules.MaxLongTextFieldLength);
        var (response, patch) = await PatchAsync($$$"""{"title":"新標題","fields":{"notes":"{{{notes}}}"}}""");

        // Assert
        Assert.That(response, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(patch.Fields["notes"].GetString(), Is.EqualTo(notes));
    }

    // 路徑和模組的 422 用同一套名字（文件裡的名字），網頁只要認一種寫法。
    [TestCase("""{"type":"bug"}""", "type", TestName = "Patch_Type_Returns400")]
    [TestCase("""{"state":"done"}""", "state", TestName = "Patch_State_Returns400")]
    [TestCase("""{"title":null}""", "title", TestName = "Patch_TitleNull_Returns400")]
    [TestCase("""{"priority":2}""", "priority", TestName = "Patch_PriorityAsNumber_Returns400")]
    [TestCase("""{"dueDate":"30/12/2026"}""", "due_date", TestName = "Patch_DateInAnotherFormat_Returns400")]
    [TestCase("""{"startDate":"2026-12-01T00:00"}""", "start_date", TestName = "Patch_StartDateWithTime_Returns400")]
    [TestCase("""{"assigneeId":"someone"}""", "assignee", TestName = "Patch_AssigneeNotAGuid_Returns400")]
    [TestCase("""{"parentKey":true}""", "parent", TestName = "Patch_ParentKeyNotAString_Returns400")]
    public async Task Patch_Rejected(string body, string expectedPath)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync(Route, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{ApiErrorCodes.BadRequest}|{expectedPath}" }));
        Assert.That(factory.Modules.Items.UpdateCalls, Is.Empty);
    }

    [Test]
    public async Task Patch_ModuleRejects_Returns422WithTheFieldPath()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnUpdate = (_, _, _) => new Failure(
            FailureKind.Invalid, WorkErrorCodes.ItemInvalidValue, "值不對。")
        {
            Details = [new FailureDetail(WorkErrorCodes.ItemInvalidValue, "impact", "不是定義過的選項。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync(Route, ApiRequests.Document("""{"fields":{"impact":"huge"}}"""));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemInvalidValue);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemInvalidValue}|impact" }));
    }

    private static async Task<(HttpStatusCode Status, WorkItemPatch Patch)> PatchAsync(string body)
    {
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var response = await client.PatchAsync(Route, ApiRequests.Document(body));
        var call = factory.Modules.Items.UpdateCalls.SingleOrDefault();
        Assert.That(call.Patch, Is.Not.Null, await response.Content.ReadAsStringAsync());
        Assert.That(call.ItemKey, Is.EqualTo("WEB-1"));
        return (response.StatusCode, call.Patch);
    }
}
