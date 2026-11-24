using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>歷史匯入的四個端點：API 把請求轉成合約的型別、把結果與失敗轉成回應，規則由工作引擎判斷。</summary>
[TestFixture]
public sealed class HistoryImportEndpointTests
{
    private static readonly Guid Author = Guid.Parse("0190a000-0000-7000-8000-0000000000b1");

    [Test]
    public async Task ImportItems_Accepted_PassesEveryFieldToTheModuleAndReturnsTheCreatedKeys()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var follower = Guid.NewGuid();
        var body = new
        {
            source = "monday",
            dryRun = false,
            rows = new object[]
            {
                new
                {
                    externalId = "m-1", type = "task", title = "舊的", createdBy = Author, createdAt = "2026-03-05T08:30:15+08:00",
                    state = "done", assigneeId = follower, priority = "high", dueDate = "2026-11-20", description = "說明",
                    parentExternalId = "m-0", followers = new[] { follower }, fields = new { impact = "高" },
                },
                new { externalId = "m-2", type = "task", title = "最少的", createdBy = Author, createdAt = "2026-03-05T00:00:00Z" },
            },
        };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/history/items", ApiRequests.Json(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var (actor, projectKey, import) = factory.Modules.History.ItemCalls.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?.AsObject().Select(p => p.Key), Is.EquivalentTo(new[] { "dryRun", "created", "skipped" }));
            Assert.That(data?["created"]?.AsArray().Select(c => c?.ToJsonString()), Is.EqualTo(new[]
            {
                """{"externalId":"m-1","itemKey":"WEB-1"}""", """{"externalId":"m-2","itemKey":"WEB-2"}""",
            }));
            Assert.That(data?["skipped"]?.AsArray(), Is.Empty);
            Assert.That((actor, projectKey, import.Source, import.DryRun, import.Rows.Count), Is.EqualTo((TestUsers.Owner.ToActor(), "WEB", "monday", false, 2)));
            var first = import.Rows[0];
            Assert.That((first.ExternalId, first.State, first.ParentExternalId, first.CreatedBy), Is.EqualTo(("m-1", "done", "m-0", Author)));
            Assert.That(first.CreatedAt, Is.EqualTo(new DateTimeOffset(2026, 3, 5, 8, 30, 15, TimeSpan.FromHours(8))));
            Assert.That(first.Followers, Is.EqualTo(new[] { follower }));
            Assert.That((first.Item.Type, first.Item.Title, first.Item.AssigneeId, first.Item.Priority), Is.EqualTo(("task", "舊的", (Guid?)follower, Priority.High)));
            Assert.That(first.Item.DueDate, Is.EqualTo(new DateOnly(2026, 11, 20)));
            Assert.That(first.Item.ParentKey, Is.Null);
            Assert.That(first.Item.Fields.ContainsKey("impact"), Is.True);
            Assert.That(import.Rows[1].Followers, Is.Empty);
        }
    }

    [Test]
    public async Task ImportItems_DryRun_PassesTheFlagAndReturnsEmptyCreated()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new { source = "monday", dryRun = true, rows = new[] { new { externalId = "m-1", type = "task", title = "一個", createdBy = Author, createdAt = "2026-03-05T00:00:00Z" } } };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/history/items", ApiRequests.Json(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.History.ItemCalls.Single().Import.DryRun, Is.True);
            Assert.That(data?["dryRun"]?.GetValue<bool>(), Is.True);
            Assert.That(data?["created"]?.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task ImportItems_RowWithParentKey_IsA400BeforeTheModuleIsCalled()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new
        {
            source = "monday",
            rows = new[] { new { externalId = "m-1", type = "task", title = "一個", createdBy = Author, createdAt = "2026-03-05T00:00:00Z", parentKey = "WEB-1" } },
        };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/history/items", ApiRequests.Json(body));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.History.ItemCalls, Is.Empty);
    }

    [Test]
    public async Task ImportItems_RowWithoutCreatedByOrCreatedAt_IsA400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new { source = "monday", rows = new[] { new { externalId = "m-1", type = "task", title = "一個" } } };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/history/items", ApiRequests.Json(body));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
    }

    [Test]
    public async Task ImportItems_InvalidRows_Returns422WithTheRowPathsFromTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.History.OnItems = (_, _, _) => new Failure(FailureKind.Invalid, WorkErrorCodes.ImportInvalid, "有問題")
        {
            Details = [new FailureDetail(WorkErrorCodes.ImportInvalid, "rows/3/parent_external_id", "找不到上層。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new { source = "monday", rows = new[] { new { externalId = "m-1", type = "task", title = "一個", createdBy = Author, createdAt = "2026-03-05T00:00:00Z" } } };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/history/items", ApiRequests.Json(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ImportInvalid);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("rows/3/parent_external_id"));
    }

    [TestCase(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Invalid, WorkErrorCodes.HistoryInvalid, HttpStatusCode.UnprocessableEntity)]
    public async Task ImportComments_ModuleFails_MapsTheFailureToTheStatusCode(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.History.OnComments = (_, _, _) => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new { source = "monday", comments = Array.Empty<object>() };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/history/comments", ApiRequests.Json(body));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task ImportComments_Accepted_PassesEveryFieldToTheModuleAndReturnsTheActivityIds()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = new
        {
            source = "monday",
            comments = new object[]
            {
                new { externalId = "u-1", itemExternalId = "m-1", authorId = Author, at = "2026-03-05T08:30:15Z", body = "第一則" },
                new { externalId = "r-1", itemExternalId = "m-1", authorId = Author, at = "2026-03-05T09:00:00Z", body = "回覆", replyToExternalId = "u-1" },
            },
        };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/history/comments", ApiRequests.Json(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var (actor, projectKey, import) = factory.Modules.History.CommentCalls.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?.AsObject().Select(p => p.Key), Is.EquivalentTo(new[] { "created", "skipped" }));
            Assert.That(data?["created"]?[0]?.ToJsonString(), Is.EqualTo("""{"externalId":"u-1","activityId":"0190a000-0000-7000-8000-0000000000c1"}"""));
            Assert.That((actor, projectKey, import.Source, import.Comments.Count), Is.EqualTo((TestUsers.Owner.ToActor(), "WEB", "monday", 2)));
            Assert.That(import.Comments[1].ReplyToExternalId, Is.EqualTo("u-1"));
            Assert.That(import.Comments[0].ReplyToExternalId, Is.Null);
            Assert.That((import.Comments[0].ItemExternalId, import.Comments[0].AuthorId, import.Comments[0].Body), Is.EqualTo(("m-1", Author, "第一則")));
            Assert.That(import.Comments[0].At, Is.EqualTo(new DateTimeOffset(2026, 3, 5, 8, 30, 15, TimeSpan.Zero)));
        }
    }

    [Test]
    public async Task Lookup_Accepted_PassesTheKindAsASnakeCaseEnumAndReturnsTheList()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            "/v1/history/lookup", ApiRequests.Json(new { source = "monday", kind = "attachment", externalIds = new[] { "a-1", "a-2" } }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var (_, lookup) = factory.Modules.History.LookupCalls.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((lookup.Source, lookup.Kind, lookup.ExternalIds), Is.EqualTo(("monday", HistoryRefKind.Attachment, new[] { "a-1", "a-2" })));
            Assert.That(data?[0]?.ToJsonString(), Is.EqualTo(
                """{"externalId":"a-1","kind":"attachment","projectKey":"WEB","itemKey":"WEB-1","targetId":"0190a000-0000-7000-8000-0000000000d1"}"""));
            Assert.That(data?.AsArray(), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Lookup_UnknownKind_IsA400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/history/lookup", ApiRequests.Json(new { source = "monday", kind = "nope", externalIds = new[] { "a" } }));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
    }

    [Test]
    public async Task ImportAttachment_NewFile_Returns201AndPassesTheFieldsAndTheContent()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/history/attachments", Upload(content: "圖片內容"u8.ToArray()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);
        var (actor, itemKey, upload, content) = factory.Modules.History.AttachmentCalls.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["fileName"]?.GetValue<string>(), Is.EqualTo("截圖.png"));
            Assert.That((actor, itemKey), Is.EqualTo((TestUsers.Owner.ToActor(), "WEB-7")));
            Assert.That((upload.Source, upload.ExternalId, upload.UploadedBy, upload.FileName, upload.ContentType), Is.EqualTo(("monday", "a-1", Author, "截圖.png", "image/png")));
            Assert.That(upload.UploadedAt, Is.EqualTo(new DateTimeOffset(2026, 3, 5, 8, 30, 15, TimeSpan.FromHours(8))));
            Assert.That(content, Is.EqualTo("圖片內容"u8.ToArray()));
        }
    }

    [Test]
    public async Task ImportAttachment_AlreadyImported_Returns200()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.History.OnAttachment = (_, key, _, _) => new HistoryAttachmentResult(FakeAttachmentService.Sample(itemKey: key), Existing: true);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/history/attachments", Upload());
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["id"]?.GetValue<Guid>(), Is.EqualTo(FakeAttachmentService.SampleId));
    }

    [TestCase("source")]
    [TestCase("externalId")]
    [TestCase("uploadedBy")]
    [TestCase("uploadedAt")]
    public async Task ImportAttachment_MissingField_IsA400(string missing)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/history/attachments", Upload(without: missing));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.History.AttachmentCalls, Is.Empty);
    }

    [TestCase("2026-03-05T08:30:15")]
    [TestCase("not a time")]
    [TestCase("2026-03-05")]
    public async Task ImportAttachment_UploadedAtWithoutAZoneOrNotATime_IsA400(string uploadedAt)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/history/attachments", Upload(uploadedAt: uploadedAt));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
    }

    [TestCase("2026-03-05T08:30:15")]
    [TestCase("2026-03-05")]
    [TestCase("not a time")]
    [TestCase("2026-03-05T08:30:15Z\n")]
    public async Task ImportItems_CreatedAtWithoutAZone_IsA400ThatSaysSoAndDoesNotCallTheModule(string createdAt)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new { source = "monday", rows = new[] { new { externalId = "m-1", type = "task", title = "一個", createdBy = Author, createdAt } } };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/history/items", ApiRequests.Json(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Has.Some.Contains("createdAt"));
            Assert.That(details.Select(d => d?["message"]?.GetValue<string>()), Has.Some.Contains("時區"));
            Assert.That(factory.Modules.History.ItemCalls, Is.Empty);
        }
    }

    [Test]
    public async Task ImportItems_CreatedAtAsANumber_IsA400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new { source = "monday", rows = new[] { new { externalId = "m-1", type = "task", title = "一個", createdBy = Author, createdAt = 1772699415 } } };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/history/items", ApiRequests.Json(body));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
    }

    [TestCase("2026-03-05T08:30:15")]
    [TestCase("2026-03-05")]
    public async Task ImportComments_AtWithoutAZone_IsA400ThatSaysSoAndDoesNotCallTheModule(string at)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new { source = "monday", comments = new[] { new { externalId = "u-1", itemExternalId = "m-1", authorId = Author, at, body = "一則" } } };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/history/comments", ApiRequests.Json(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Has.Some.Contains("at"));
            Assert.That(details.Select(d => d?["message"]?.GetValue<string>()), Has.Some.Contains("時區"));
            Assert.That(factory.Modules.History.CommentCalls, Is.Empty);
        }
    }

    [Test]
    public async Task ImportAttachment_UnknownTextField_IsA400BeforeTheModuleIsCalled()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("x"), "extra");
        content.Add(FilePart([1, 2, 3]), "file", "截圖.png");

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/history/attachments", content);

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.History.AttachmentCalls, Is.Empty);
    }

    [Test]
    public async Task ImportAttachment_RepeatedKnownField_IsA400BecauseTheNumberOfFieldsIsLimited()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        for (var repeat = 0; repeat < 20; repeat++)
        {
            content.Add(new StringContent("monday"), "source");
        }

        content.Add(FilePart([1, 2, 3]), "file", "截圖.png");

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/history/attachments", content);

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.History.AttachmentCalls, Is.Empty);
    }

    [Test]
    public async Task ImportAttachment_FieldsAfterTheFile_IsA400BecauseTheFileIsAStream()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(FilePart([1, 2, 3]), "file", "截圖.png");
        content.Add(new StringContent("monday"), "source");

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/history/attachments", content);

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
    }

    [Test]
    public async Task ImportAttachment_NotMultipart_IsA415()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/history/attachments", ApiRequests.Json(new { }));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
    }

    [Test]
    public async Task ImportAttachment_ModuleFails_MapsTheFailure()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.History.OnAttachment = (_, _, _, _) => new Failure(FailureKind.Invalid, WorkErrorCodes.AttachmentTooLarge, "太大");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/items/WEB-7/history/attachments", Upload());

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, WorkErrorCodes.AttachmentTooLarge);
    }

    private static MultipartFormDataContent Upload(
        byte[]? content = null, string? without = null, string uploadedAt = "2026-03-05T08:30:15+08:00")
    {
        var form = new MultipartFormDataContent();
        var fields = new (string Name, string Value)[]
        {
            ("source", "monday"),
            ("externalId", "a-1"),
            ("uploadedBy", Author.ToString("D")),
            ("uploadedAt", uploadedAt),
        };
        foreach (var (name, value) in fields.Where(f => f.Name != without))
        {
            form.Add(new StringContent(value), name);
        }

        form.Add(FilePart(content ?? [1, 2, 3]), "file", "截圖.png");
        return form;
    }

    private static ByteArrayContent FilePart(byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return part;
    }
}
