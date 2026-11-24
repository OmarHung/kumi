using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Work.Blueprints;
using PmTool.Work.Templates;

namespace PmTool.Work.Tests.Projects;

/// <summary>範例檔的 cycles、milestones 與項目的 cycle、milestone、estimate：讀取、驗證，以及建立專案時一起建立。</summary>
[TestFixture]
public sealed class ExamplePlanningTests
{
    private const string Template = "simple_tasks";
    private static readonly DateOnly Today = new(2026, 11, 18);
    private static readonly TemplateCatalog Catalog = new(BlueprintValidator.CreateDefault());

    private const string Valid = """
        cycles:
          - { ref: sprint_prev, name: 衝刺 1, startInDays: -14, endInDays: -1 }
          - { ref: sprint_now,  name: 衝刺 2, startInDays: 0,   endInDays: 13 }
        milestones:
          - { ref: v1, name: v1.0 上線, dueInDays: 28, description: 第一個對外的版本。 }
          - { ref: v2, name: v2.0, dueInDays: 60 }
        items:
          - { type: task, title: 範例：登入, cycle: sprint_now, milestone: v1, estimate: 3 }
          - { type: task, title: 範例：沒排進去 }
        """;

    private static ProjectSchema Schema => ProjectSchema.From(Catalog.Find(Template)!.Blueprint);

    private static ExampleReadResult Read(string yaml) => ExampleReader.Read(Template, yaml, Schema, Today);

    [Test]
    public void Read_CyclesMilestonesAndItemReferences_AreParsed()
    {
        // Act
        var result = Read(Valid);

        // Assert
        Assert.That(result.Problems, Is.Empty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Cycles.Select(c => (c.Ref, c.Name, c.StartInDays, c.EndInDays)), Is.EqualTo(new[]
            {
                ("sprint_prev", "衝刺 1", -14, -1),
                ("sprint_now", "衝刺 2", 0, 13),
            }));
            Assert.That(result.Milestones.Select(m => (m.Ref, m.Name, m.DueInDays, m.Description)), Is.EqualTo(new (string, string, int, string?)[]
            {
                ("v1", "v1.0 上線", 28, "第一個對外的版本。"),
                ("v2", "v2.0", 60, null),
            }));
            Assert.That((result.Items[0].Cycle, result.Items[0].Milestone, result.Items[0].Estimate), Is.EqualTo(("sprint_now", "v1", (decimal?)3)));
            Assert.That((result.Items[1].Cycle, result.Items[1].Milestone, result.Items[1].Estimate), Is.EqualTo(((string?)null, (string?)null, (decimal?)null)));
        }
    }

    [Test]
    public void Read_FileWithoutCyclesOrMilestones_StillWorks()
    {
        // Act
        var result = Read("items:\n  - type: task\n    title: A\n    estimate: 2.5");

        // Assert
        Assert.That(result.Problems, Is.Empty);
        Assert.That((result.Cycles, result.Milestones), Is.EqualTo((Array.Empty<ExampleCycle>(), Array.Empty<ExampleMilestone>())));
        Assert.That(result.Items.Single().Estimate, Is.EqualTo(2.5m));
    }

    [Test]
    public void Resolve_Estimate_IsTheNormalizedNumber()
    {
        // Act
        var resolved = Read("items:\n  - type: task\n    title: A\n    estimate: 3.50").Items.Single().Resolve(Schema, Today);

        // Assert
        Assert.That(resolved.Estimate, Is.EqualTo(3.5m));
    }

    [TestCase("cycles:\n  - { ref: a, name: 衝刺, startInDays: 0, endInDays: 5 }\n  - { ref: a, name: 衝刺二, startInDays: 6, endInDays: 9 }", "週期", "第 2 筆", "ref")]
    [TestCase("cycles:\n  - { ref: a, name: 衝刺, startInDays: 0, endInDays: 5 }\n  - { ref: b, name: 衝刺, startInDays: 6, endInDays: 9 }", "週期", "第 2 筆", "name")]
    [TestCase("cycles:\n  - { ref: a, name: 衝刺, startInDays: 0, endInDays: 5 }\n  - { ref: b, name: 衝刺二, startInDays: 5, endInDays: 9 }", "週期", "第 2 筆", "startInDays")]
    [TestCase("cycles:\n  - { ref: a, name: 衝刺, startInDays: 5, endInDays: 4 }", "週期", "第 1 筆", "endInDays")]
    [TestCase("cycles:\n  - { ref: a, name: 衝刺, startInDays: 0, endInDays: 365 }", "週期", "第 1 筆", "endInDays")]
    [TestCase("cycles:\n  - { name: 衝刺, startInDays: 0, endInDays: 5 }", "週期", "第 1 筆", "ref")]
    [TestCase("cycles:\n  - { ref: a, startInDays: 0, endInDays: 5 }", "週期", "第 1 筆", "name")]
    [TestCase("cycles:\n  - { ref: a, name: 衝刺, startInDays: 0 }", "週期", "第 1 筆", "endInDays")]
    [TestCase("cycles:\n  - { ref: a, name: 衝刺, startInDays: 0, endInDays: 5, color: red }", "週期", "第 1 筆", "color")]
    [TestCase("cycles:\n  - { ref: a, name: \"衝\\u200B刺\", startInDays: 0, endInDays: 5 }", "週期", "第 1 筆", "name")]
    [TestCase("cycles:\n  - { ref: \"\", name: 衝刺, startInDays: 0, endInDays: 5 }", "週期", "第 1 筆", "ref")]
    [TestCase("cycles:\n  - { ref: \"  \", name: 衝刺, startInDays: 0, endInDays: 5 }", "週期", "第 1 筆", "ref")]
    [TestCase("cycles: 3", "cycles", "清單", "cycles")]
    [TestCase("milestones:\n  - { ref: a, name: v1, dueInDays: 5 }\n  - { ref: a, name: v2, dueInDays: 6 }", "里程碑", "第 2 筆", "ref")]
    [TestCase("milestones:\n  - { ref: a, name: v1, dueInDays: 5 }\n  - { ref: b, name: V1, dueInDays: 6 }", "里程碑", "第 2 筆", "name")]
    [TestCase("milestones:\n  - { ref: \"\", name: v1, dueInDays: 5 }", "里程碑", "第 1 筆", "ref")]
    [TestCase("milestones:\n  - { ref: a, name: v1 }", "里程碑", "第 1 筆", "dueInDays")]
    [TestCase("milestones:\n  - { ref: a, name: v1, dueInDays: 99999 }", "里程碑", "第 1 筆", "dueInDays")]
    [TestCase("milestones:\n  - { ref: a, name: v1, dueInDays: 5, description: 5 }", "里程碑", "第 1 筆", "description")]
    [TestCase("milestones:\n  - { ref: a, name: v1, dueInDays: 5, startInDays: 1 }", "里程碑", "第 1 筆", "startInDays")]
    public void Read_BadCycleOrMilestone_NamesTheTemplateTheKindThePositionAndTheField(string extra, string kind, string position, string path)
    {
        // Act
        var result = Read(extra + "\nitems:\n  - { type: task, title: A }");

        // Assert
        Assert.That(result.Problems, Is.Not.Empty);
        Assert.That(result.Problems[0], Does.Contain(Template).And.Contain(kind).And.Contain(position).And.Contain(path));
    }

    [Test]
    public void Read_NamesThatOnlyDifferInCaseFoldingTheDatabaseDoesNotApply_AreNotDuplicates()
    {
        // Arrange：資料庫的 lower("ς") 還是 "ς"、lower("σ") 是 "σ"，兩個是不同的名稱；OrdinalIgnoreCase 會把它們當成同一個。
        const string sigmas =
            "cycles:\n  - { ref: a, name: \"ς\", startInDays: 0, endInDays: 5 }\n  - { ref: b, name: \"σ\", startInDays: 6, endInDays: 9 }\n"
            + "milestones:\n  - { ref: a, name: \"ς\", dueInDays: 5 }\n  - { ref: b, name: \"σ\", dueInDays: 6 }";

        // Act
        var result = Read(sigmas + "\nitems:\n  - { type: task, title: A }");

        // Assert
        Assert.That(result.Problems, Is.Empty);
    }

    [TestCase("cycle: nope", "cycle")]
    [TestCase("milestone: nope", "milestone")]
    [TestCase("cycle: 5", "cycle")]
    [TestCase("estimate: -1", "estimate")]
    [TestCase("estimate: lots", "estimate")]
    public void Read_BadItemReference_ReportsTheField(string entry, string path)
    {
        // Act
        var result = Read($"cycles:\n  - {{ ref: a, name: 衝刺, startInDays: 0, endInDays: 5 }}\nitems:\n  - {{ type: task, title: A, {entry} }}");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 1 筆").And.Contain(path));
    }

    [Test]
    public void Read_ItemReferencesAMilestoneRefThroughCycle_Fails()
    {
        // Act（ref 各自一份：里程碑的 ref 不能拿來當週期用）
        var result = Read(
            "milestones:\n  - { ref: v1, name: v1, dueInDays: 5 }\nitems:\n  - { type: task, title: A, cycle: v1 }");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 1 筆").And.Contain("cycle").And.Contain("v1"));
    }

    [Test]
    public void Read_TooManyCyclesOrMilestones_Fails()
    {
        // Arrange
        var cycles = string.Join("\n", Enumerable.Range(0, ExampleReader.MaxPlanningEntries + 1)
            .Select(i => $"  - {{ ref: c{i}, name: 衝刺 {i}, startInDays: {i * 2}, endInDays: {i * 2} }}"));

        // Act
        var result = Read($"cycles:\n{cycles}\nitems:\n  - {{ type: task, title: A }}");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("cycles").And.Contain($"{ExampleReader.MaxPlanningEntries}"));
    }

    [Test]
    public async Task CreateAsync_WithAPlanningExample_CreatesCyclesMilestonesAndAssignsTheItems()
    {
        // Arrange：用自訂的模板目錄，內容是 simple_tasks 的藍圖加上有週期與里程碑的範例。
        var template = Catalog.Find(Template)!;
        var read = Read(Valid);
        Assert.That(read.Problems, Is.Empty);
        var custom = new TemplateCatalog([template with { Examples = read.Items, Cycles = read.Cycles, Milestones = read.Milestones }]);
        await using var host = await WorkTestHost.StartAsync(services => services.AddSingleton(custom));
        var scenario = new Scenario(host);

        // Act
        var created = await host.Projects.CreateAsync(
            scenario.Admin, new NewProject("EX", "含範例", Template, IncludeExamples: true), CancellationToken.None);
        var cycles = await host.Planning.ListCyclesAsync(scenario.Admin, "EX", CancellationToken.None);
        var milestones = await host.Planning.ListMilestonesAsync(scenario.Admin, "EX", CancellationToken.None);
        var items = (await host.Items.ListAsync(scenario.Admin, "EX", new WorkItemQuery(), CancellationToken.None)).Value.Items;

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        var created0 = DateOnly.FromDateTime(WorkTestHost.StartTime.UtcDateTime);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                cycles.Value.Select(c => (c.Name, c.StartDate, c.EndDate, c.Progress.Total)),
                Is.EqualTo(new[]
                {
                    ("衝刺 1", created0.AddDays(-14), created0.AddDays(-1), 0),
                    ("衝刺 2", created0, created0.AddDays(13), 1),
                }));
            Assert.That(
                milestones.Value.Select(m => (m.Name, m.DueDate, m.Description, m.Progress.Total)),
                Is.EqualTo(new (string, DateOnly, string?, int)[]
                {
                    ("v1.0 上線", created0.AddDays(28), "第一個對外的版本。", 1),
                    ("v2.0", created0.AddDays(60), null, 0),
                }));
            Assert.That((items[0].CycleId, items[0].MilestoneId, items[0].Estimate), Is.EqualTo((cycles.Value[1].Id, (Guid?)milestones.Value[0].Id, (decimal?)3)));
            Assert.That((items[1].CycleId, items[1].MilestoneId), Is.EqualTo(((Guid?)null, (Guid?)null)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatedBeforeMidnightInUtcButAfterInTheOrganizationZone_ExamplesStartOnTheOrganizationsDay()
    {
        // Arrange：UTC 11/1 16:30 在 UTC+8 已經是 11/2 00:30。
        var template = Catalog.Find(Template)!;
        var read = Read(Valid);
        var custom = new TemplateCatalog([template with { Examples = read.Items, Cycles = read.Cycles, Milestones = read.Milestones }]);
        await using var host = await WorkTestHost.StartAsync(services => services.AddSingleton(custom));
        host.Clock.Advance(new DateTimeOffset(2026, 11, 1, 16, 30, 0, TimeSpan.Zero) - WorkTestHost.StartTime);
        var scenario = new Scenario(host);

        // Act
        await host.Projects.CreateAsync(
            scenario.Admin, new NewProject("EX", "含範例", Template, IncludeExamples: true), CancellationToken.None);
        var cycles = await host.Planning.ListCyclesAsync(scenario.Admin, "EX", CancellationToken.None);

        // Assert
        var today = new DateOnly(2026, 11, 2);
        Assert.That(cycles.Value.Select(c => c.StartDate), Is.EqualTo(new[] { today.AddDays(-14), today }));
    }

    [Test]
    public async Task CreateAsync_WithoutIncludeExamples_CreatesNoCyclesOrMilestones()
    {
        // Arrange
        var template = Catalog.Find(Template)!;
        var read = Read(Valid);
        var custom = new TemplateCatalog([template with { Examples = read.Items, Cycles = read.Cycles, Milestones = read.Milestones }]);
        await using var host = await WorkTestHost.StartAsync(services => services.AddSingleton(custom));
        var scenario = new Scenario(host);

        // Act
        await host.Projects.CreateAsync(scenario.Admin, new NewProject("EX", "不含範例", Template), CancellationToken.None);
        var cycles = await host.Planning.ListCyclesAsync(scenario.Admin, "EX", CancellationToken.None);

        // Assert
        Assert.That(cycles.Value, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_ProjectKeyTaken_LeavesNoCyclesBehindInTheOtherProject()
    {
        // Arrange
        var template = Catalog.Find(Template)!;
        var read = Read(Valid);
        var custom = new TemplateCatalog([template with { Examples = read.Items, Cycles = read.Cycles, Milestones = read.Milestones }]);
        await using var host = await WorkTestHost.StartAsync(services => services.AddSingleton(custom));
        var scenario = new Scenario(host);
        await host.Projects.CreateAsync(scenario.Admin, new NewProject("EX", "第一個", Template, IncludeExamples: true), CancellationToken.None);

        // Act
        var duplicate = await host.Projects.CreateAsync(
            scenario.Admin, new NewProject("EX", "重複", Template, IncludeExamples: true), CancellationToken.None);
        var cycles = await host.Planning.ListCyclesAsync(scenario.Admin, "EX", CancellationToken.None);

        // Assert
        Assert.That(duplicate.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectKeyTaken));
        Assert.That(cycles.Value, Has.Count.EqualTo(2));
    }
}
