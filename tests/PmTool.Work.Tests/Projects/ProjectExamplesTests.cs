using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Work.Templates;

namespace PmTool.Work.Tests.Projects;

[TestFixture]
public sealed class ProjectExamplesTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private int ExampleCountOf(string key) => _host.Projects.ListTemplates().Single(t => t.Key == key).ExampleCount;

    private async Task<IReadOnlyList<WorkItem>> ItemsOfAsync(Scenario scenario, string projectKey)
    {
        var page = await _host.Items.ListAsync(
            scenario.Admin, projectKey, new WorkItemQuery { Limit = WorkItemQuery.MaxLimit }, CancellationToken.None);
        Assert.That(page.Succeeded, Is.True, () => page.Failure!.Message);
        return page.Value.Items;
    }

    // 測試來源是靜態的，拿不到 _host；目錄和 ListTemplates() 用的是同一份，key 一樣。
    private static readonly string[] AllTemplateKeys =
        [.. new TemplateCatalog(BlueprintValidator.CreateDefault()).Summaries.Select(s => s.Key)];

    [TestCaseSource(nameof(AllTemplateKeys))]
    public async Task CreateAsync_WithExamples_CreatesOneItemPerExampleInFileOrderWithTheBlueprintsStateCategories(string template)
    {
        // Arrange
        var scenario = new Scenario(_host);
        var expected = ExampleCountOf(template);

        // Act
        var created = await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("EX", "含範例", template, IncludeExamples: true), CancellationToken.None);
        var items = await ItemsOfAsync(scenario, "EX");

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(expected));
            Assert.That(items.Select(i => i.Number), Is.EqualTo(Enumerable.Range(1, expected).Select(n => (long)n)));
            Assert.That(items.Select(i => i.Title), Has.All.Not.Empty);
        }

        var blueprint = (await _host.Projects.GetAsync(scenario.Admin, "EX", CancellationToken.None)).Value.Blueprint;
        using (Assert.EnterMultipleScope())
        {
            foreach (var item in items)
            {
                var type = blueprint.ItemTypes.Single(t => t.Key == item.Type);
                var state = blueprint.Workflows.Single(w => w.Key == type.Workflow).States.Single(s => s.Key == item.State);
                Assert.That(item.StateCategory, Is.EqualTo(state.Category), $"{template}：{item.Key}");
            }
        }
    }

    [Test]
    public async Task CreateAsync_WithExamples_SetsStatesCategoriesAndParents()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("EX", "含範例", Scenario.AgencyProject, IncludeExamples: true), CancellationToken.None);
        var items = await ItemsOfAsync(scenario, "EX");
        var detail = (await _host.Projects.GetAsync(scenario.Admin, "EX", CancellationToken.None)).Value;

        // Assert：每個項目的狀態都在自己類型的流程裡，類別跟著狀態；有上層的上層編號比自己小而且存在。
        var blueprint = detail.Blueprint;
        using (Assert.EnterMultipleScope())
        {
            foreach (var item in items)
            {
                var type = blueprint.ItemTypes.Single(t => t.Key == item.Type);
                var state = blueprint.Workflows.Single(w => w.Key == type.Workflow).States.Single(s => s.Key == item.State);
                Assert.That(item.StateCategory, Is.EqualTo(state.Category), item.Key);
                if (item.ParentKey is { } parentKey)
                {
                    var parent = items.Single(i => i.Key == parentKey);
                    Assert.That(parent.Number, Is.LessThan(item.Number), item.Key);
                    Assert.That(type.Parents, Contains.Item(parent.Type), item.Key);
                }
            }

            Assert.That(items.Any(i => i.ParentKey is not null), Is.True, "範例要示範上下層");
            Assert.That(items.Select(i => i.State).Distinct().Count(), Is.GreaterThan(1), "範例要示範不同的狀態");
            Assert.That(items.Any(i => i.DueDate is { } due && due < DateOnly.FromDateTime(WorkTestHost.StartTime.UtcDateTime)), Is.True, "範例要有一筆過期");
        }
    }

    [Test]
    public async Task CreateAsync_WithExamples_EveryItemHasCreatedActivityAndEventByTheCreator()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("EX", "含範例", Scenario.SimpleTasks, IncludeExamples: true), CancellationToken.None);
        var items = await ItemsOfAsync(scenario, "EX");
        var events = (await EventFeed.OfAsync(_host, scenario)).Where(e => e.Kind == WorkEventKind.ItemCreated).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(events.Select(e => e.Item.ItemKey), Is.EqualTo(items.Select(i => i.Key)));
            Assert.That(events.Select(e => e.ActorPersonId), Has.All.EqualTo(scenario.Admin.PersonId));
            Assert.That(events.Select(e => e.Item.ActivityId), Has.All.Not.Null);
            foreach (var item in items)
            {
                var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);
                Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }), item.Key);
                Assert.That(events.Single(e => e.Item.ItemKey == item.Key).Item.ActivityId, Is.EqualTo(activities[0].Id), item.Key);
            }
        }
    }

    [Test]
    public async Task CreateAsync_WithExamples_NextItemContinuesTheNumbering()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var count = ExampleCountOf(Scenario.SimpleTasks);
        await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("EX", "含範例", Scenario.SimpleTasks, IncludeExamples: true), CancellationToken.None);

        // Act
        var next = await scenario.CreateTaskAsync("EX", "自己的第一筆");

        // Assert
        Assert.That(next.Number, Is.EqualTo(count + 1));
    }

    [Test]
    public async Task CreateAsync_WithoutExamples_CreatesNoItems()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("EX", "不含範例", Scenario.AgencyProject), CancellationToken.None);
        var items = await ItemsOfAsync(scenario, "EX");
        var events = await EventFeed.OfAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Is.Empty);
            Assert.That(events, Is.Empty);
        }
    }

    [Test]
    public async Task CreateAsync_DuplicateKeyWithExamples_LeavesNoExamplesBehind()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("DUP");

        // Act
        var second = await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject("DUP", "重複", Scenario.SimpleTasks, IncludeExamples: true), CancellationToken.None);
        var items = await ItemsOfAsync(scenario, "DUP");
        var events = await EventFeed.OfAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectKeyTaken));
            Assert.That(items, Is.Empty);
            Assert.That(events, Is.Empty);
        }
    }
}
