using PmTool.Cli.Import.Monday;

namespace PmTool.Cli.Tests;

/// <summary>plan 的猜法：人員依 Email、欄依種類、優先度依英文名稱、群組標題的月份；其餘留白並寫註解。</summary>
[TestFixture]
public sealed class MondayPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static MondayColumnValue Status(string column, string? label) =>
        new(column, "status", label, null, label, [], []);

    private static MondayColumnValue People(string column, params string[] ids) =>
        new(column, "people", null, null, null, ids, []);

    private static MondayItem Item(string id, string group, string? status, string? priority = null, string creator = "1001",
        IReadOnlyList<MondayUpdate>? updates = null, IReadOnlyList<MondayItem>? subitems = null, string state = "active") =>
        new(id, $"Example {id}", group, state, Now, creator, $"https://example.monday.test/{id}",
            [Status("status", status), Status("prio", priority), People("person", "1002")], subitems ?? [], updates ?? [], []);

    private static MondayUpdate Comment(string id, string creator, int replies = 0) =>
        new(id, creator, Now, "<p>x</p>", [], [.. Enumerable.Range(0, replies).Select(n => new MondayReply($"{id}r{n}", creator, Now, "<p>y</p>", []))]);

    private static MondayDump Dump(params MondayItem[] items) => new(
        "/srv/example/dump",
        Now,
        [new("1001", "Example PM", "PM@example.com"), new("1002", "Example Dev", "dev@example.com"), new("1003", "Former Staff", null), new("1004", "Unused Person", "unused@example.com")],
        [new MondayBoard(
            "123", "Example Cases",
            [new("g_m11", "2026年11月例行維護"), new("g_case", "Example 官網改版"), new("g_old", "Temp")],
            [
                new("name", "Name", "name", []),
                new("status", "Status", "status", ["Working on it", "Done", "Stuck"]),
                new("prio", "Priority", "status", ["High", "Critical", "Foo"]),
                new("person", "Owner", "people", []),
                new("date5", "Due Date", "date", []),
                new("date9", "Other Date", "date", []),
                new("timeline", "Timeline", "timeline", []),
                new("numbers7", "Hrs", "numbers", []),
                new("client", "Client", "board_relation", []),
                new("files", "Files", "file", []),
                new("subitems", "Subitems", "subtasks", []),
            ],
            [new("sstatus", "Status", "status", ["Todo", "Done"]), new("sperson", "Owner", "people", [])],
            items)]);

    private static (MondayMapping Mapping, string Yaml, MappingAnnotations Notes) Plan(MondayDump dump, params string[] emails)
    {
        var draft = MondayPlanner.Draft(dump, emails, "./monday-dump");
        var path = Path.Combine(Path.GetTempPath(), $"pm-plan-{Guid.NewGuid():N}.yaml");
        try
        {
            MondayMappingFile.Save(path, draft.Mapping, draft.Annotations);
            return (draft.Mapping, File.ReadAllText(path), draft.Annotations);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Draft_MatchesPeopleByEmailIgnoringCase_AndListsOnlyThoseWhoAppear()
    {
        // Arrange
        var dump = Dump(
            Item("1", "g_case", "Done", updates: [Comment("u1", "1001", replies: 2), Comment("u2", "1003")]),
            Item("2", "g_case", "Done", creator: "1003"));

        // Act
        var (mapping, yaml, _) = Plan(dump, "pm@example.com", "dev@example.com");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.People.Keys, Is.EqualTo(new[] { "1001", "1003", "1002" }), "留言多的在前，沒出現過的人不列");
            Assert.That(mapping.People["1001"], Is.EqualTo("pm@example.com"), "用這裡的 Email 的寫法");
            Assert.That(mapping.People["1002"], Is.EqualTo("dev@example.com"));
            Assert.That(mapping.People["1003"], Is.Null);
            Assert.That(yaml, Does.Contain("Example PM（3 則留言）"));
            Assert.That(yaml, Does.Contain("Former Staff（1 則留言）").And.Contain("monday 沒有給 Email"));
            Assert.That(yaml, Does.Contain("Example Dev（沒有留言）"));
            Assert.That(yaml, Does.Not.Contain("Unused Person"));
        }
    }

    [Test]
    public void Draft_GuessesColumnsByKind_AndSeparatesPriorityFromStatus()
    {
        // Arrange
        var dump = Dump(Item("1", "g_case", "Done"));

        // Act
        var (mapping, yaml, _) = Plan(dump);

        // Assert
        var board = mapping.Boards["123"];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(board.StatusColumn, Is.EqualTo("status"));
            Assert.That(board.PriorityColumn, Is.EqualTo("prio"));
            Assert.That(board.OwnerColumn, Is.EqualTo("person"));
            Assert.That(board.DueColumn, Is.EqualTo("date5"), "第一個 date");
            Assert.That(board.TimelineColumn, Is.EqualTo("timeline"));
            Assert.That(board.Fields.Keys, Is.EqualTo(new[] { "date9", "numbers7", "client" }), "其他支援的欄列出來，值留空");
            Assert.That(board.Fields.Values.All(field => field.Field.Length == 0), Is.True);
            Assert.That(yaml, Does.Contain("Hrs（numbers）").And.Contain("Client（board_relation）"));
            Assert.That(yaml, Does.Contain("Files（file）"), "不支援的欄在註解裡說明");
            Assert.That(yaml, Does.Not.Match(@"numbers7: +\w"), "沒填的欄位不寫值");
        }
    }

    [Test]
    public void Draft_PrioritiesGuessedFromEnglishNames_OthersLeftBlank()
    {
        // Arrange
        var dump = Dump(
            Item("1", "g_case", "Done", priority: "High"),
            Item("2", "g_case", "Done", priority: "Critical"),
            Item("3", "g_case", "Done", priority: "Foo"),
            Item("4", "g_case", "Done"));

        // Act
        var (mapping, yaml, _) = Plan(dump);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.Boards["123"].Priorities, Is.EqualTo(new Dictionary<string, string> { ["High"] = "high", ["Critical"] = "urgent", ["Foo"] = string.Empty }));
            Assert.That(yaml, Does.Contain("\"High\": high").And.Contain("\"Critical\": urgent"));
        }
    }

    [Test]
    public void Draft_StatesListEveryLabelThatAppearsWithCounts_ValuesBlank()
    {
        // Arrange
        var subtask = new MondayItem("s1", "Example sub", string.Empty, "active", Now, "1001", "u", [Status("sstatus", "Todo")], [], [], []);
        var dump = Dump(
            Item("1", "g_case", "Done", subitems: [subtask]),
            Item("2", "g_case", "Done"),
            Item("3", "g_case", "Working on it"),
            Item("4", "g_case", null),
            Item("5", "g_case", "Removed label"),
            Item("6", "g_case", "Stuck", state: "deleted"));

        // Act
        var (mapping, yaml, _) = Plan(dump);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.Boards["123"].States.Keys, Is.EqualTo(new[] { "Working on it", "Done", "Todo", "Removed label", string.Empty }));
            Assert.That(mapping.Boards["123"].States.Values.All(value => value.Length == 0), Is.True);
            Assert.That(yaml, Does.Contain("\"Done\":").And.Contain("2 個項目"));
            Assert.That(yaml, Does.Contain("\"Todo\":").And.Contain("1 個子項目"));
            Assert.That(yaml, Does.Contain("\"\":").And.Contain("1 個項目"));
            Assert.That(yaml, Does.Not.Contain("Stuck"), "已刪除的項目不算");
        }
    }

    [Test]
    public void Draft_GroupsAreAllListedWithCountsAndMonthGroupsGetACycle()
    {
        // Arrange
        var dump = Dump(Item("1", "g_m11", "Done"), Item("2", "g_m11", "Done"), Item("3", "g_case", "Done"));

        // Act
        var (mapping, yaml, _) = Plan(dump);

        // Assert
        var groups = mapping.Boards["123"].Groups;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups.Keys, Is.EqualTo(new[] { "g_m11", "g_case", "g_old" }));
            Assert.That(groups["g_m11"].Cycle, Is.EqualTo(new CycleMapping("2026 年 11 月", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30))));
            Assert.That(groups["g_case"].Cycle, Is.Null);
            Assert.That(groups.Values.All(group => group.Project is null && group.Type is null && !group.Skip), Is.True);
            Assert.That(yaml, Does.Contain("2026年11月例行維護（2 個）").And.Contain("Temp（0 個）"));
        }
    }

    [TestCase("2026年2月 維運", 2026, 2, 28)]
    [TestCase("2024 年 2 月", 2024, 2, 29)]
    [TestCase("2026年12月項目", 2026, 12, 31)]
    public void GuessCycle_UsesTheWholeMonth(string title, int year, int month, int lastDay)
    {
        // Arrange & Act
        var cycle = MondayBoardPlanner.GuessCycle(title);

        // Assert
        Assert.That(cycle, Is.EqualTo(new CycleMapping($"{year} 年 {month} 月", new DateOnly(year, month, 1), new DateOnly(year, month, lastDay))));
    }

    [TestCase("Example 官網改版")]
    [TestCase("2026年13月")]
    [TestCase("1月")]
    public void GuessCycle_NoMonthInTheTitle_IsNull(string title) =>
        Assert.That(MondayBoardPlanner.GuessCycle(title), Is.Null);

    [Test]
    public void Draft_SubitemsAreOnlyWrittenAsAComment_SoTheDraftStillLoads()
    {
        // Arrange
        var subtask = new MondayItem("s1", "Example sub", string.Empty, "active", Now, "1001", "u", [], [], [], []);
        var dump = Dump(Item("1", "g_case", "Done", subitems: [subtask]));

        // Act
        var (mapping, yaml, _) = Plan(dump);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.Boards["123"].Subitems, Is.Null);
            Assert.That(yaml, Does.Contain("# subitems:").And.Contain("#   statusColumn: sstatus").And.Contain("1 個"));
        }
    }

    [Test]
    public void Draft_TheSavedFileLoadsBack_WithBlankEntriesDropped()
    {
        // Arrange
        var dump = Dump(Item("1", "g_m11", "Done", priority: "High"));
        var draft = MondayPlanner.Draft(dump, ["pm@example.com"], "./monday-dump");
        var path = Path.Combine(Path.GetTempPath(), $"pm-plan-{Guid.NewGuid():N}.yaml");

        // Act
        MondayMappingFile.Save(path, draft.Mapping, draft.Annotations);
        var loaded = MondayMappingFile.Load(path);
        File.Delete(path);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.People["1001"], Is.EqualTo("pm@example.com"));
            Assert.That(loaded.Boards["123"].StatusColumn, Is.EqualTo("status"));
            Assert.That(loaded.Boards["123"].States, Is.Empty);
            Assert.That(loaded.Boards["123"].Fields, Is.Empty);
            Assert.That(loaded.Boards["123"].Priorities, Is.EqualTo(new Dictionary<string, string> { ["High"] = "high" }));
            Assert.That(loaded.Boards["123"].Groups, Has.Count.EqualTo(3));
            Assert.That(loaded.Boards["123"].Groups["g_m11"].Cycle, Is.Not.Null);
        }
    }

    [Test]
    public void Draft_NamesWithNewlinesAreNeutralisedInComments()
    {
        // Arrange
        var dump = Dump(Item("1", "g_case", "Done")) with { Users = [new("1001", "Evil\nincludeArchived: true", "pm@example.com"), new("1002", "Dev", null)] };

        // Act
        var (_, yaml, _) = Plan(dump);

        // Assert
        Assert.That(yaml.Split('\n').Where(line => line.StartsWith("includeArchived", StringComparison.Ordinal)), Has.Exactly(1).Items);
    }
}
