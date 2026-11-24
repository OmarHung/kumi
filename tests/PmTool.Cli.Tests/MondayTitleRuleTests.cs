using System.Text.Json.Nodes;
using PmTool.Cli.Import.Monday;
using PmTool.Cli.Import.Monday.Run;
using static PmTool.Cli.Tests.ImportFixtures;

namespace PmTool.Cli.Tests;

/// <summary>依標題的規則補類型與欄位、群組的 fields、<c>cycle: monthly</c>：檢查階段的問題、套用順序、週期、彙總的提醒。</summary>
[TestFixture]
public sealed class MondayTitleRuleTests
{
    private FakeServer _server = null!;

    [SetUp]
    public void SetUp() => _server = Server(out _);

    [TearDown]
    public void TearDown() => _server.Dispose();

    private static string[] Messages(Notes notes) => [.. notes.ToList().Select(note => note.Message)];

    private static TitleRule Rule(string[] match, string? type = null, string? project = null, params (string Key, string Value)[] fields) =>
        new() { Project = project, Match = match, Type = type, Fields = fields.ToDictionary(pair => pair.Key, pair => pair.Value) };

    /// <summary>只有 g2（維運，MNT、變更）的對應；規則放在看板層，客戶的欄可以另外對應。</summary>
    private static MondayMapping Maintenance(
        IReadOnlyList<TitleRule> rules, GroupMapping? group = null, Func<BoardMapping, BoardMapping>? change = null) =>
        Mapping(board => (change?.Invoke(board) ?? board) with
        {
            TitleRules = rules,
            Groups = new Dictionary<string, GroupMapping> { ["g2"] = group ?? new GroupMapping { Project = "MNT", Type = "change" } },
        });

    private static JsonObject RowOf(ImportPlan plan, string project, string externalId) =>
        plan.Projects[project].Items.Single(item => item.ExternalId == externalId).Row;

    private static string? ClientOf(ImportPlan plan, string externalId) =>
        RowOf(plan, "MNT", externalId)["fields"]?["client"]?.GetValue<string>();

    private static readonly FieldMapping ClientColumn = new()
    {
        Field = "client",
        Values = new Dictionary<string, string> { ["Example 站"] = "client_a" },
    };

    // 類型

    [Test]
    public async Task Type_FirstMatchingRuleWithATypeWins_IgnoringCase_AndOthersKeepTheGroupType()
    {
        // Arrange
        var rules = new[] { Rule(["漏洞"], "security"), Rule(["EXA"], fields: ("client", "client_b")), Rule(["cve"], "task") };
        var dump = Dump(
            Item("1", "Example CVE-1 漏洞", group: "g2"), Item("2", "example CvE-2", group: "g2"),
            Item("3", "Example 一般修改", group: "g2"), Item("4", "EXA 憑證更新", group: "g2"));

        // Act
        var plan = await PlanAsync(_server, dump, Maintenance(rules));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
            Assert.That(RowOf(plan, "MNT", "1")["type"]!.GetValue<string>(), Is.EqualTo("security"));
            Assert.That(RowOf(plan, "MNT", "2")["type"]!.GetValue<string>(), Is.EqualTo("task"));
            Assert.That(RowOf(plan, "MNT", "3")["type"]!.GetValue<string>(), Is.EqualTo("change"));
            Assert.That(RowOf(plan, "MNT", "4")["type"]!.GetValue<string>(), Is.EqualTo("change"), "沒有 type 的規則不改類型");
        }
    }

    [Test]
    public async Task Type_RuleWithAProject_OnlyAppliesToItemsGoingToThatProject()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            TitleRules = [Rule(["Example"], "security", project: "mnt")],
            Groups = new Dictionary<string, GroupMapping>
            {
                ["g1"] = new() { Project = "CASE", Type = "task" },
                ["g2"] = new() { Project = "MNT", Type = "change" },
            },
        });

        // Act
        var plan = await PlanAsync(_server, Dump(Item("1", group: "g1"), Item("2", group: "g2")), mapping);

        // Assert
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(RowOf(plan, "CASE", "1")["type"]!.GetValue<string>(), Is.EqualTo("task"));
        Assert.That(RowOf(plan, "MNT", "2")["type"]!.GetValue<string>(), Is.EqualTo("security"));
    }

    [Test]
    public async Task Type_IsNotAppliedToSubitems()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            TitleRules = [Rule(["Example"], "security")],
            Subitems = new SubitemMapping { Type = "task", StatusColumn = "status" },
            Groups = new Dictionary<string, GroupMapping> { ["g2"] = new() { Project = "MNT", Type = "change", Fields = new Dictionary<string, string> { ["client"] = "client_a" } } },
        });
        var dump = Dump(Item("1", "Example 上層", group: "g2", subitems: [Item("20", "Example 子項目")]));

        // Act
        var plan = await PlanAsync(_server, dump, mapping);

        // Assert
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(RowOf(plan, "MNT", "1")["type"]!.GetValue<string>(), Is.EqualTo("security"));
        var subitem = RowOf(plan, "MNT", "20");
        Assert.That(subitem["type"]!.GetValue<string>(), Is.EqualTo("task"));
        Assert.That(subitem["fields"]?["client"], Is.Null, "群組的欄位預設值也不給子項目");
    }

    [Test]
    public async Task Type_ChangedToATypeWithoutTheMondayField_DropsThatFieldInsteadOfFailingTheItem()
    {
        // Arrange
        var mapping = Maintenance(
            [Rule(["漏洞"], "security")], change: board => board with { Fields = new Dictionary<string, FieldMapping> { ["client"] = ClientColumn }, LinkField = "monday_url" });
        var dump = Dump(
            Item("1", "Example 漏洞", group: "g2", extra: Relation("client", "Example 站")),
            Item("2", "Example 一般", group: "g2", extra: Relation("client", "Example 站")));

        // Act
        var plan = await PlanAsync(_server, dump, mapping);

        // Assert
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(ClientOf(plan, "1"), Is.Null, "資安類型沒有「客戶」欄位");
        Assert.That(RowOf(plan, "MNT", "1")["fields"]!["monday_url"], Is.Not.Null);
        Assert.That(ClientOf(plan, "2"), Is.EqualTo("client_a"));
    }

    // 欄位的優先順序

    [Test]
    public async Task Fields_MondayColumnBeatsGroupBeatsRule()
    {
        // Arrange
        var group = new GroupMapping { Project = "MNT", Type = "change", Fields = new Dictionary<string, string> { ["client"] = "client_b" } };
        var mapping = Maintenance([Rule(["EXA"], fields: ("client", "client_a"))], group, board => board with { Fields = new Dictionary<string, FieldMapping> { ["client"] = ClientColumn } });
        var dump = Dump(
            Item("1", "EXA 憑證", group: "g2", extra: Relation("client", "Example 站")),
            Item("2", "EXA 沒填客戶", group: "g2"),
            Item("3", "Example 沒填客戶", group: "g2", extra: Relation("client", "對不上的客戶")));

        // Act
        var plan = await PlanAsync(_server, dump, mapping);

        // Assert
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(ClientOf(plan, "1"), Is.EqualTo("client_a"), "monday 的欄");
        Assert.That(ClientOf(plan, "2"), Is.EqualTo("client_b"), "群組的 fields 比規則優先");
        Assert.That(ClientOf(plan, "3"), Is.EqualTo("client_b"), "monday 的欄對不上時退到群組");
    }

    [Test]
    public async Task Fields_RuleFillsWhenNothingElseHasAValue_AndFirstMatchingRulePerFieldWins()
    {
        // Arrange
        var rules = new[] { Rule(["EXA"], fields: ("client", "client_b")), Rule(["ACME", "憑證"], fields: ("client", "client_a"), project: "MNT") };
        var dump = Dump(Item("1", "EXA 憑證", group: "g2"), Item("2", "沒有關鍵字", group: "g2"));

        // Act
        var plan = await PlanAsync(_server, dump, Maintenance(rules));

        // Assert
        Assert.That(ClientOf(plan, "1"), Is.EqualTo("client_b"));
        Assert.That(ClientOf(plan, "2"), Is.Null);
    }

    [Test]
    public async Task Fields_TheGroupValuesAreConvertedLikeMondayOnes_SelectMultiSelectCheckboxNumberAndText()
    {
        // Arrange
        var fields = new Dictionary<string, string>
        {
            ["client"] = "client_a", ["tags"] = "t1, t2", ["approved"] = "true", ["actual_hours"] = "1.5", ["summary"] = "固定文字",
        };
        var mapping = Mapping(board => board with { Groups = new Dictionary<string, GroupMapping> { ["g1"] = new() { Project = "CASE", Type = "task", Fields = fields } } });

        // Act
        var plan = await PlanAsync(_server, Dump(Item("1")), mapping);

        // Assert
        var row = RowOf(plan, "CASE", "1")["fields"]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
            Assert.That(row["client"]!.GetValue<string>(), Is.EqualTo("client_a"));
            Assert.That(row["tags"]!.AsArray().Select(tag => tag!.GetValue<string>()), Is.EqualTo(new[] { "t1", "t2" }));
            Assert.That(row["approved"]!.GetValue<bool>(), Is.True);
            Assert.That(row["actual_hours"]!.GetValue<decimal>(), Is.EqualTo(1.5m));
            Assert.That(row["summary"]!.GetValue<string>(), Is.EqualTo("固定文字"));
        }
    }

    [Test]
    public async Task Fields_OnTheFinalTypeMissing_AreSkippedWithAReminder()
    {
        // Arrange
        var rules = new[] { Rule(["漏洞"], "security"), Rule(["漏洞"], fields: ("client", "client_a")) };
        var group = new GroupMapping { Project = "MNT", Type = "change", Fields = new Dictionary<string, string> { ["client"] = "client_b" } };

        // Act
        var plan = await PlanAsync(_server, Dump(Item("1", "Example 漏洞", group: "g2")), Maintenance(rules, group));

        // Assert
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(ClientOf(plan, "1"), Is.Null);
        Assert.That(Messages(plan.Reminders), Has.Some.Contain("類型「security」沒有欄位「client」"));
    }

    // 檢查階段

    [Test]
    public async Task Check_RuleTypeMustExistInTheProjectsThatUseIt_AndUnusedProjectsAreNotReported()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            TitleRules = [Rule(["a"], "nope"), Rule(["b"], "nope2", project: "MNT"), Rule(["c"], "nope3", project: "OTHER")],
            Groups = new Dictionary<string, GroupMapping>
            {
                ["g1"] = new() { Project = "CASE", Type = "task" },
                ["g2"] = new() { Project = "MNT", Type = "change" },
            },
        });

        // Act
        var plan = await PlanAsync(_server, Dump(Item("1", group: "g1"), Item("2", group: "g2")), mapping);

        // Assert
        var messages = Messages(plan.Issues);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages, Has.Some.Contain("專案 CASE 沒有項目類型「nope」").And.Contain("titleRules 第 1 條"));
            Assert.That(messages, Has.Some.Contain("專案 MNT 沒有項目類型「nope」"));
            Assert.That(messages, Has.Some.Contain("專案 MNT 沒有項目類型「nope2」"));
            Assert.That(messages, Has.None.Contain("nope2").And.Contain("CASE"));
            Assert.That(messages, Has.None.Contain("nope3"));
        }
    }

    [Test]
    public async Task Check_FieldsMustBeOnTheRulesTypeAndValuesMustBeValid_ListedTogether()
    {
        // Arrange
        var rules = new[]
        {
            Rule(["a"], "security", fields: ("client", "client_a")),
            Rule(["b"], fields: ("client", "client_x")),
            Rule(["c"], fields: ("tags", "t1, t9")),
            Rule(["d"], fields: ("actual_hours", "很多")),
            Rule(["e"], fields: ("nofield", "x")),
        };
        var group = new GroupMapping { Project = "MNT", Type = "change", Fields = new Dictionary<string, string> { ["client"] = "client_z" } };

        // Act
        var plan = await PlanAsync(_server, Dump(Item("1", group: "g2")), Maintenance(rules, group));

        // Assert
        var messages = Messages(plan.Issues);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages, Has.Some.Contain("類型「security」沒有欄位「client」").And.Contain("titleRules 第 1 條"));
            Assert.That(messages, Has.Some.Contain("沒有選項「client_x」").And.Contain("titleRules 第 2 條"));
            Assert.That(messages, Has.Some.Contain("類型「change」沒有欄位「tags」"), "tags 只掛在任務上");
            Assert.That(messages, Has.Some.Contain("類型「change」沒有欄位「actual_hours」"));
            Assert.That(messages, Has.Some.Contain("沒有欄位「nofield」"));
            Assert.That(messages, Has.Some.Contain("沒有選項「client_z」").And.Contain("群組「2026年11月例行維護"));
        }
    }

    [Test]
    public async Task Check_MultiSelectEveryValueMustBeAnOption_AndNumberAndCheckboxMustParse()
    {
        // Arrange
        var fields = new Dictionary<string, string> { ["tags"] = "t1, t9", ["approved"] = "也許", ["actual_hours"] = "x" };
        var mapping = Mapping(board => board with { Groups = new Dictionary<string, GroupMapping> { ["g1"] = new() { Project = "CASE", Type = "task", Fields = fields } } });

        // Act
        var plan = await PlanAsync(_server, Dump(Item("1")), mapping);

        // Assert
        var messages = Messages(plan.Issues);
        Assert.That(messages, Has.Some.Contain("沒有選項「t9」"));
        Assert.That(messages, Has.Some.Contain("要寫 true 或 false"));
        Assert.That(messages, Has.Some.Contain("不是數字"));
        Assert.That(messages, Has.None.Contain("t1」"));
    }

    // 依建立月份的週期

    private static MondayItem CreatedAt(MondayItem item, string moment) => item with { CreatedAt = DateTimeOffset.Parse(moment) };

    private static GroupMapping Monthly => new() { Project = "MNT", Type = "change", Cycle = CycleMapping.MonthlyByCreation };

    [Test]
    public async Task Monthly_UsesTheTaipeiMonthOfTheCreationTime_AtTheMonthBoundary()
    {
        // Arrange
        var dump = Dump(
            CreatedAt(Item("1", group: "g2"), "2026-10-31T15:59:59Z"),
            CreatedAt(Item("2", group: "g2"), "2026-10-31T16:00:00Z"),
            CreatedAt(Item("3", group: "g2"), "2026-12-31T16:00:00Z"));

        // Act
        var plan = await PlanAsync(_server, dump, Maintenance([], Monthly));

        // Assert
        var project = plan.Projects["MNT"];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
            Assert.That(project.Items.Select(item => item.CycleName), Is.EqualTo(new[] { "2026 年 10 月", "2026 年 11 月", "2027 年 1 月" }));
            Assert.That(project.Cycles.Keys, Is.EquivalentTo(new[] { "2026 年 10 月", "2026 年 11 月", "2027 年 1 月" }));
            Assert.That(project.Cycles["2026 年 10 月"], Is.EqualTo(new CycleMapping("2026 年 10 月", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))));
            Assert.That(project.Cycles["2027 年 1 月"].End, Is.EqualTo(new DateOnly(2027, 1, 31)));
        }
    }

    [Test]
    public async Task Monthly_SubitemsFollowTheirParent()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            Subitems = new SubitemMapping { Type = "task", StatusColumn = "status" },
            Groups = new Dictionary<string, GroupMapping> { ["g2"] = Monthly },
        });
        var parent = CreatedAt(Item("1", group: "g2", subitems: [CreatedAt(Item("20"), "2026-12-15T00:00:00Z")]), "2026-10-20T00:00:00Z");

        // Act
        var plan = await PlanAsync(_server, Dump(parent), mapping);

        // Assert
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(plan.Projects["MNT"].Items.Select(item => (item.ExternalId, item.CycleName)), Is.EqualTo(new[] { ("1", "2026 年 10 月"), ("20", "2026 年 10 月") }));
        Assert.That(plan.Projects["MNT"].Cycles.Keys, Is.EqualTo(new[] { "2026 年 10 月" }));
    }

    [Test]
    public async Task Monthly_SharesTheCycleWithAFixedCycleOfTheSameName_AndItIsPlannedOnce()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            Groups = new Dictionary<string, GroupMapping>
            {
                ["g1"] = Monthly,
                ["g2"] = new() { Project = "MNT", Type = "change", Cycle = MondayBoardPlanner.MonthCycle(2026, 11) },
            },
        });
        var dump = Dump(Item("1", group: "g1"), Item("2", group: "g2"));

        // Act
        var plan = await PlanAsync(_server, dump, mapping);

        // Assert
        Assert.That(plan.Projects["MNT"].Cycles.Keys, Is.EqualTo(new[] { "2026 年 11 月" }));
        Assert.That(plan.Projects["MNT"].Items.Select(item => item.CycleName), Is.All.EqualTo("2026 年 11 月"));
        Assert.That(Messages(plan.Reminders), Has.None.Contain("不同的日期"));
    }

    // 報告

    [Test]
    public async Task Reminders_AreSummarisedPerTypeAndPerField_NotPerItem()
    {
        // Arrange
        var rules = new[] { Rule(["漏洞"], "security"), Rule(["CVE"], "task"), Rule(["EXA"], fields: ("client", "client_a")) };
        var dump = Dump(
            Item("1", "漏洞 A", group: "g2"), Item("2", "漏洞 B", group: "g2"), Item("3", "CVE C", group: "g2"),
            Item("4", "EXA 憑證", group: "g2"), Item("5", "EXA 網站", group: "g2"), Item("6", "沒有關鍵字", group: "g2"));
        var group = new GroupMapping { Project = "MNT", Type = "change", Cycle = CycleMapping.MonthlyByCreation };

        // Act
        var plan = await PlanAsync(_server, dump, Maintenance(rules, group));

        // Assert
        var messages = Messages(plan.Reminders);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages, Has.One.EqualTo("依標題的規則：2 個項目的類型改成「資安」"));
            Assert.That(messages, Has.One.EqualTo("依標題的規則：1 個項目的類型改成「任務」"));
            Assert.That(messages, Has.One.EqualTo("依標題的規則補了 2 個「客戶」"));
            Assert.That(messages, Has.One.EqualTo("依建立的月份放進週期：6 個項目、1 個週期"));
        }
    }

    [Test]
    public async Task DryRunReport_ShowsTheSummaries()
    {
        // Arrange
        var plan = await PlanAsync(_server, Dump(Item("1", "漏洞 A", group: "g2")), Maintenance([Rule(["漏洞"], "security")], Monthly));
        var report = new ImportReport(dryRun: true, plan.Issues, plan.Reminders);
        using var output = new StringWriter();

        // Act
        await report.WriteTextAsync(output);

        // Assert
        Assert.That(output.ToString(), Does.Contain("依標題的規則：1 個項目的類型改成「資安」").And.Contain("依建立的月份放進週期：1 個項目、1 個週期"));
    }

    [Test]
    public async Task NoRulesAndNoMonthly_ProduceNoSummaries()
    {
        // Act
        var plan = await PlanAsync(_server, Dump(Item("1", "漏洞 A", group: "g2")), Maintenance([]));

        // Assert
        Assert.That(Messages(plan.Reminders), Has.None.Contain("依標題").And.None.Contain("依建立的月份"));
    }
}
