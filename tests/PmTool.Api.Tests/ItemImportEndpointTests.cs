using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>試算表匯入：API 把請求轉成合約的型別、把結果與失敗轉成回應，規則由工作引擎判斷。</summary>
[TestFixture]
public sealed class ItemImportEndpointTests
{
    [Test]
    public async Task Import_Accepted_PassesEveryRowFieldAndTheStateToTheModuleAndReturnsTheKeys()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var assignee = Guid.NewGuid();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = new
        {
            rows = new object[]
            {
                new
                {
                    type = "task", title = "第一個", assigneeId = assignee, priority = "high", startDate = "2026-11-02",
                    dueDate = "2026-11-20", estimate = 3.5, parentKey = "WEB-1", description = "說明", state = "done",
                    fields = new { impact = "高" },
                },
                new { type = "task", title = "第二個" },
            },
            dryRun = false,
        };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items/import", ApiRequests.Json(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var (actor, projectKey, import) = factory.Modules.Import.Calls.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?.AsObject().Select(p => p.Key), Is.EquivalentTo(new[] { "dryRun", "count", "createdKeys" }));
            Assert.That(data?["count"]?.GetValue<int>(), Is.EqualTo(2));
            Assert.That(data?["createdKeys"]?.AsArray().Select(k => k?.GetValue<string>()), Is.EqualTo(new[] { "WEB-1", "WEB-2" }));
            Assert.That((actor, projectKey, import.DryRun, import.Rows.Count), Is.EqualTo((TestUsers.Owner.ToActor(), "WEB", false, 2)));
            Assert.That(import.Rows[0].State, Is.EqualTo("done"));
            Assert.That(import.Rows[0].Item.Title, Is.EqualTo("第一個"));
            Assert.That(import.Rows[0].Item.AssigneeId, Is.EqualTo(assignee));
            Assert.That(import.Rows[0].Item.Priority, Is.EqualTo(Priority.High));
            Assert.That(import.Rows[0].Item.DueDate, Is.EqualTo(new DateOnly(2026, 11, 20)));
            Assert.That(import.Rows[0].Item.ParentKey, Is.EqualTo("WEB-1"));
            Assert.That(import.Rows[0].Item.Fields.ContainsKey("impact"), Is.True);
            Assert.That(import.Rows[1].State, Is.Null);
        }
    }

    [Test]
    public async Task Import_DryRun_PassesTheFlagAndReturnsAnEmptyKeyList()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects/WEB/items/import", ApiRequests.Json(new { rows = new[] { new { type = "task", title = "一個" } }, dryRun = true }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.Import.Calls.Single().Import.DryRun, Is.True);
            Assert.That(data?["dryRun"]?.GetValue<bool>(), Is.True);
            Assert.That(data?["count"]?.GetValue<int>(), Is.EqualTo(1));
            Assert.That(data?["createdKeys"]?.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task Import_InvalidRows_Returns422WithTheRowPathsFromTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Import.OnImport = (_, _, _) => new Failure(FailureKind.Invalid, WorkErrorCodes.ImportInvalid, "有問題")
        {
            Details = [new FailureDetail(WorkErrorCodes.ItemTitleRequired, "rows/3/title", "請填標題。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects/WEB/items/import", ApiRequests.Json(new { rows = new[] { new { type = "task", title = "" } } }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ImportInvalid);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("rows/3/title"));
    }

    [TestCase(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    public async Task Import_ModuleFails_MapsTheFailureToTheStatusCode(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Import.OnImport = (_, _, _) => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items/import", ApiRequests.Json(new { rows = Array.Empty<object>() }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task Import_UnknownProperty_Returns400AndDoesNotCallTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects/WEB/items/import", ApiRequests.Json(new { rows = new[] { new { type = "task", title = "一個", surprise = 1 } } }));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.Import.Calls, Is.Empty);
    }

    [Test]
    public async Task Import_NullRow_PassesAnEmptyRowToTheModuleInsteadOfFailing()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            "/v1/projects/WEB/items/import", new StringContent("""{"rows":[null]}""", System.Text.Encoding.UTF8, "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.Import.Calls.Single().Import.Rows, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Import_BodyLargerThanTheGeneralLimitButWithinTheImportLimit_IsAccepted()
    {
        // Arrange：一列的說明就比一般端點的上限大。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = $$"""{"rows":[{"type":"task","title":"長","description":"{{new string('x', ApiLimits.MaxJsonBodyBytes + 1)}}"}]}""";

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items/import", ApiRequests.Document(body));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.Import.Calls, Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Import_BodyOverTheImportLimit_Returns413WithTheCodeAndTheLimitInTheMessage(bool chunked)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var bytes = System.Text.Encoding.UTF8.GetBytes($$"""{"rows":[],"pad":"{{new string('x', ApiLimits.MaxImportBodyBytes)}}"}""");
        const string route = "/v1/projects/WEB/items/import";
        using var request = chunked
            ? ApiRequests.Chunked(route, bytes)
            : new HttpRequestMessage(HttpMethod.Post, route) { Content = ApiRequests.Bytes(bytes) };

        // Act
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain(ApiLimits.MaxImportBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(factory.Modules.Import.Calls, Is.Empty);
        }
    }

    private const string ImportRoute = "/v1/projects/WEB/items/import";

    [Test]
    public async Task Import_WithoutItemCreate_Returns403AndReadsNoBodyBytes()
    {
        // Arrange
        CountingStream? counter = null;
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { WrapRequestBody = body => counter = new CountingStream(body) });
        factory.Modules.Access.OnFindVisible = (_, key) => Visible(key, isArchived: false, WorkPermissions.ItemRead);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(ImportRoute, ApiRequests.Json(new { rows = new[] { new { type = "task", title = "一個" } } }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(counter?.BytesRead ?? 0, Is.Zero, "沒權限的請求，內文一個位元組都不讀");
            Assert.That(factory.Modules.Import.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Import_ArchivedProject_Returns409AndReadsNoBodyBytes()
    {
        // Arrange
        CountingStream? counter = null;
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { WrapRequestBody = body => counter = new CountingStream(body) });
        factory.Modules.Access.OnFindVisible = (_, key) => Visible(key, isArchived: true, WorkPermissions.ItemRead);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(ImportRoute, ApiRequests.Json(new { rows = Array.Empty<object>() }));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(counter?.BytesRead ?? 0, Is.Zero);
    }

    [Test]
    public async Task Import_ProjectNotVisible_Returns404ProjectNotFoundAndReadsNoBodyBytes()
    {
        // Arrange
        CountingStream? counter = null;
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { WrapRequestBody = body => counter = new CountingStream(body) });
        factory.Modules.Access.OnFindVisible = (_, _) => null;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(ImportRoute, ApiRequests.Json(new { rows = Array.Empty<object>() }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        Assert.That(counter?.BytesRead ?? 0, Is.Zero);
    }

    [Test]
    public async Task Import_ThirdConcurrentImportFromOnePerson_Returns429()
    {
        // Arrange：兩個匯入卡在讀內文。
        var stalled = new StalledBodies();
        var stall = false;
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { WrapRequestBody = body => Volatile.Read(ref stall) ? stalled.CreateStream() : body });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        Volatile.Write(ref stall, true);
        var first = client.PostAsync(ImportRoute, ApiRequests.Json(new { rows = Array.Empty<object>() }));
        var second = client.PostAsync(ImportRoute, ApiRequests.Json(new { rows = Array.Empty<object>() }));
        await stalled.WaitForStalledRequestsAsync(2, TimeSpan.FromSeconds(20));

        // Act
        using var third = await client.PostAsync(ImportRoute, ApiRequests.Json(new { rows = Array.Empty<object>() }));
        stalled.Open();
        using var firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(20));
        using var secondResponse = await second.WaitAsync(TimeSpan.FromSeconds(20));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(third, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
    }

    private static ProjectAccessSummary Visible(string key, bool isArchived, params string[] permissions) =>
        new(FakeWorkAccessQueries.ProjectId, key.ToUpperInvariant(), "範例專案", isArchived, permissions);
}
