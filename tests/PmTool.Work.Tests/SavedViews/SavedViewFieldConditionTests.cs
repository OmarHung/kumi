using PmTool.Abstractions;
using static PmTool.Work.Tests.SavedViews.SavedViewCrudTests;

namespace PmTool.Work.Tests.SavedViews;

/// <summary>檢視的欄位條件與分組：存得下、讀得回，格式不對的擋下，舊資料沒有這兩個屬性時讀出來是 null。</summary>
[TestFixture]
public sealed class SavedViewFieldConditionTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_FieldsGroupAndFieldSort_AreStoredAndListedBack()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var person = Guid.NewGuid().ToString();
        var query = Empty with
        {
            Fields = new Dictionary<string, string>
            {
                ["category"] = "bug,chore",
                ["tags"] = "_none",
                ["reviewer"] = $"me,{person}",
                ["blocked"] = "true",
            },
            Group = "field.category",
            Sort = "field.story_points:desc",
        };

        // Act
        var created = await Create(scenario.Admin, "依分類", query: query);
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        var stored = listed.Value.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.Fields, Is.EquivalentTo(query.Fields!));
            Assert.That((stored.Group, stored.Sort), Is.EqualTo(("field.category", "field.story_points:desc")));
        }
    }

    [TestCase("state")]
    [TestCase("assignee")]
    [TestCase("field.client")]
    public async Task CreateAsync_ValidGroups_AreAccepted(string group)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await Create(scenario.Admin, "分組", query: Empty with { Group = group });

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    [TestCase("priority", "query.group")]
    [TestCase("field", "query.group")]
    [TestCase("field.", "query.group")]
    [TestCase("field.Client", "query.group")]
    [TestCase("field.x", "query.group")]
    public async Task CreateAsync_BadGroup_IsInvalidWithThePath(string group, string path)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await Create(scenario.Admin, "壞的", query: Empty with { Group = group });

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { path }));
    }

    [TestCase("field:asc")]
    [TestCase("field:desc")]
    [TestCase("field.:asc")]
    [TestCase("field.Client:asc")]
    [TestCase("field.client:up")]
    public async Task CreateAsync_BareFieldOrBadFieldSort_IsInvalid(string sort)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await Create(scenario.Admin, "壞的排序", query: Empty with { Sort = sort });

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "query.sort" }));
        Assert.That(result.Failure.Details[0].Message, Does.Not.Contain("、field、").And.Not.Contain("field、"));
    }

    [TestCase("Client", "x1")]
    [TestCase("c", "x1")]
    [TestCase("client", "")]
    [TestCase("client", "a,,b")]
    [TestCase("client", "Bad Value")]
    [TestCase("client", "bug\0")]
    [TestCase("client", "not-a-guid-xx")]
    public async Task CreateAsync_BadFields_IsInvalid(string key, string value)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await Create(
            scenario.Admin, "壞的", query: Empty with { Fields = new Dictionary<string, string> { [key] = value } });

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(result.Failure!.Details.Single().Path, Does.StartWith("query.fields"));
    }

    [Test]
    public async Task CreateAsync_TooManyFieldsOrValues_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var fields = Enumerable.Range(0, FieldFilter.MaxPerQuery + 1).ToDictionary(i => $"fld{i}", _ => "a1");
        var values = string.Join(',', Enumerable.Range(0, FieldFilter.MaxValues + 1).Select(i => $"opt{i}"));

        // Act
        var tooManyFields = await Create(scenario.Admin, "多欄位", query: Empty with { Fields = fields });
        var tooManyValues = await Create(
            scenario.Admin, "多值", query: Empty with { Fields = new Dictionary<string, string> { ["client"] = values } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tooManyFields.Failure!.Details.Single().Path, Is.EqualTo("query.fields"));
            Assert.That(tooManyValues.Failure!.Details.Single().Path, Is.EqualTo("query.fields.client"));
        }
    }

    [Test]
    public async Task ListAsync_ViewStoredBeforeFieldsExisted_ReadsFieldsAndGroupAsNull()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await Create(scenario.Admin, "舊的", query: Empty with { Type = "task" });

        // Act
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Value.Single().Query.Fields, Is.Null);
            Assert.That(listed.Value.Single().Query.Group, Is.Null);
        }
    }

    private Task<Result<SavedView>> Create(Actor actor, string name, SavedViewQuery query) =>
        _host.SavedViews.CreateAsync(actor, "WEB", new SavedViewDraft(name, SavedViewLayout.Table, query), CancellationToken.None);
}
