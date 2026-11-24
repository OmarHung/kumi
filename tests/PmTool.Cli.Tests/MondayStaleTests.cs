using PmTool.Cli.Import.Monday;
using PmTool.Cli.Import.Monday.Run;
using static PmTool.Cli.Tests.ImportFixtures;

namespace PmTool.Cli.Tests;

/// <summary>很久沒動、還沒結束的項目改用 stale 的狀態：對應檔讀寫、哪些項目會換、檢查、彙總的提醒、plan 的草稿。</summary>
[TestFixture]
public sealed class MondayStaleTests
{
    // 項目建立在 2026-11-01 02:03 UTC（台北 10:03）；這一天之前（台北時間）才算舊的。
    private static readonly StaleRule Rule = new(new DateOnly(2026, 11, 2), "dropped");
    private static readonly IReadOnlyList<MondayUpdate> RecentComment = [Update("u1", at: "2026-11-02T03:00:00Z")];

    private FakeServer _server = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _server = Server(out _);
        _directory = Path.Combine(Path.GetTempPath(), $"pm-stale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        _server.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private string Write(string yaml)
    {
        var path = Path.Combine(_directory, "monday.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private static string StateOf(ImportPlan plan, string externalId) =>
        plan.Projects["CASE"].Items.Single(item => item.ExternalId == externalId).Row["state"]?.GetValue<string>() ?? "(none)";

    private static Task<ImportPlan> PlanWith(FakeServer server, MondayDump dump, StaleRule? stale = null, Func<BoardMapping, BoardMapping>? change = null) =>
        PlanAsync(server, dump, Mapping(board => (change?.Invoke(board) ?? board) with { Stale = stale ?? Rule }));

    private static string[] Messages(Notes notes) => [.. notes.ToList().Select(note => note.Message)];

    // 對應檔的讀寫

    [Test]
    public void Load_Stale_ReadsDateAndState()
    {
        // Arrange
        var path = Write("dump: ./d\nboards:\n  \"1\":\n    stale:\n      before: 2026-09-01\n      state: canceled\n");

        // Act
        var stale = MondayMappingFile.Load(path).Boards["1"].Stale;

        // Assert
        Assert.That(stale, Is.EqualTo(new StaleRule(new DateOnly(2026, 9, 1), "canceled")));
    }

    [Test]
    public void Load_WithoutStale_IsNull() =>
        Assert.That(MondayMappingFile.Load(Write("dump: ./d\nboards:\n  \"1\":\n    stale:\n")).Boards["1"].Stale, Is.Null);

    [TestCase("      before: 2026/09/01\n      state: canceled\n", 5, "不是日期")]
    [TestCase("      before: soon\n      state: canceled\n", 5, "不是日期")]
    [TestCase("      state: canceled\n", 5, "缺少 before")]
    [TestCase("      before: 2026-09-01\n", 5, "缺少 state")]
    [TestCase("      before: 2026-09-01\n      state: canceled\n      extra: 1\n", 7, "不認得的鍵")]
    public void Load_BadStale_SaysWhichLine(string body, int line, string expectedPart)
    {
        // Arrange
        var path = Write($"dump: ./d\nboards:\n  \"1\":\n    stale:\n{body}");

        // Act
        var failure = Assert.Throws<CliException>(() => MondayMappingFile.Load(path));

        // Assert
        Assert.That(failure!.Message, Does.Contain($"第 {line} 行").And.Contain(expectedPart));
    }

    [Test]
    public void SaveThenLoad_KeepsStale()
    {
        // Arrange
        var mapping = Mapping(board => board with { Stale = new StaleRule(new DateOnly(2026, 9, 1), "canceled") });
        var path = Path.Combine(_directory, "out.yaml");

        // Act
        MondayMappingFile.Save(path, mapping);
        var text = File.ReadAllText(path);
        var again = MondayMappingFile.Load(path);

        // Assert
        Assert.That(text, Does.Contain("    stale:\n      before: 2026-09-01\n      state: canceled\n"));
        Assert.That(again.Boards[Board].Stale, Is.EqualTo(new StaleRule(new DateOnly(2026, 9, 1), "canceled")));
    }

    // plan 的草稿

    [Test]
    public void Draft_WritesACommentedStaleBlock_ThatTakesEffectWhenUncommented()
    {
        // Arrange
        var draft = MondayPlanner.Draft(Dump(Item("1")), [], "./monday-dump");
        var path = Path.Combine(_directory, "draft.yaml");

        // Act
        MondayMappingFile.Save(path, draft.Mapping, draft.Annotations);
        var yaml = File.ReadAllText(path);
        var commented = MondayMappingFile.Load(path).Boards[Board].Stale;
        var enabled = yaml
            .Replace("    # stale:", "    stale:").Replace("    #   before:", "      before:").Replace("    #   state:", "      state:");
        var uncommented = MondayMappingFile.Load(Write(enabled)).Boards[Board].Stale;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(yaml, Does.Contain("    # stale:\n    #   before: 2026-08-01\n    #   state: canceled"), "現在（11/1）往前 90 天是 8/3，取那個月的 1 號");
            Assert.That(yaml, Does.Contain("我的工作"));
            Assert.That(commented, Is.Null, "註解掉的不生效");
            Assert.That(uncommented, Is.EqualTo(new StaleRule(new DateOnly(2026, 8, 1), "canceled")));
        }
    }

    // 狀態換成取消

    [Test]
    public async Task OldItems_WithoutStatusOrStillInProgress_BecomeCanceled()
    {
        // Arrange
        var dump = Dump(Item("blank", status: null), Item("doing", status: "Working on it"));

        // Act
        var plan = await PlanWith(_server, dump);

        // Assert
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(new[] { StateOf(plan, "blank"), StateOf(plan, "doing") }, Is.EqualTo(new[] { "dropped", "dropped" }));
    }

    [Test]
    public async Task OldItem_WhenMappingHasNoStatusColumn_BecomesCanceled()
    {
        // Act
        var plan = await PlanWith(_server, Dump(Item("1")), change: board => board with { StatusColumn = null });

        // Assert
        Assert.That(StateOf(plan, "1"), Is.EqualTo("dropped"));
    }

    [Test]
    public async Task FinishedItems_AreNotChanged()
    {
        // Act
        var plan = await PlanWith(_server, Dump(Item("1", status: "Done")));

        // Assert
        Assert.That(StateOf(plan, "1"), Is.EqualTo("done"));
        Assert.That(plan.Reminders.ToList().Where(note => note.Kind == NoteKinds.Stale), Is.Empty);
    }

    [Test]
    public async Task RecentCommentOrReply_KeepsTheItemAsMapped()
    {
        // Arrange
        var withReply = Item("reply", status: "Working on it", updates: [Update("u2", at: "2026-10-01T00:00:00Z", replies: [Reply("r1", at: "2026-11-05T00:00:00Z")])]);
        var dump = Dump(Item("comment", status: "Working on it", updates: RecentComment), withReply);

        // Act
        var plan = await PlanWith(_server, dump);

        // Assert
        Assert.That(new[] { StateOf(plan, "comment"), StateOf(plan, "reply") }, Is.EqualTo(new[] { "doing", "doing" }));
    }

    [Test]
    public async Task Cutoff_IsMidnightInTheOrganizationTimeZone()
    {
        // Arrange：before 是 2026-11-02，台北的 00:00 是 UTC 11/01 16:00。
        var justBefore = Item("before", status: "Working on it") with { CreatedAt = DateTimeOffset.Parse("2026-11-01T15:59:00Z") };
        var atCutoff = Item("at", status: "Working on it") with { CreatedAt = DateTimeOffset.Parse("2026-11-01T16:00:00Z") };

        // Act
        var plan = await PlanWith(_server, Dump(justBefore, atCutoff));

        // Assert
        Assert.That(new[] { StateOf(plan, "before"), StateOf(plan, "at") }, Is.EqualTo(new[] { "dropped", "doing" }));
    }

    [Test]
    public async Task Subitems_AreJudgedByTheirOwnLastActivity()
    {
        // Arrange
        var parent = Item(
            "p", status: "Done",
            subitems: [Item("old", group: string.Empty, status: "Working on it"), Item("fresh", group: string.Empty, status: "Working on it", updates: RecentComment)]);
        var subitems = new SubitemMapping { Type = "task", StatusColumn = "status" };

        // Act
        var plan = await PlanWith(_server, Dump(parent), change: board => board with { Subitems = subitems });

        // Assert
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(new[] { StateOf(plan, "p"), StateOf(plan, "old"), StateOf(plan, "fresh") }, Is.EqualTo(new[] { "done", "dropped", "doing" }));
    }

    [Test]
    public async Task WithoutStale_StatesStayAsMapped()
    {
        // Act
        var plan = await PlanAsync(_server, Dump(Item("1", status: "Working on it")), Mapping());

        // Assert
        Assert.That(StateOf(plan, "1"), Is.EqualTo("doing"));
        Assert.That(plan.Reminders.ToList().Where(note => note.Kind == NoteKinds.Stale), Is.Empty);
    }

    // 檢查階段

    [Test]
    public async Task StaleState_NotInTheWorkflow_IsAnIssue()
    {
        // Act
        var plan = await PlanWith(_server, Dump(Item("1")), new StaleRule(new DateOnly(2026, 11, 2), "gone"));

        // Assert
        Assert.That(Messages(plan.Issues), Has.Some.Contain("專案 CASE 的類型「task」的流程裡沒有狀態「gone」").And.Contain("stale.state"));
    }

    [Test]
    public async Task StaleState_NotInTheSubitemTypeWorkflow_IsAnIssue()
    {
        // Arrange：子項目的類型是 change，同一個流程，所以這裡換成不存在的類型狀態 → 以不存在的狀態驗證兩邊都檢查。
        var subitems = new SubitemMapping { Type = "change", StatusColumn = "status" };

        // Act
        var plan = await PlanWith(_server, Dump(Item("p", subitems: [Item("s", group: string.Empty)])), new StaleRule(new DateOnly(2026, 11, 2), "gone"), board => board with { Subitems = subitems });

        // Assert
        var messages = Messages(plan.Issues);
        Assert.That(messages, Has.Some.Contain("類型「task」的流程裡沒有狀態「gone」"));
        Assert.That(messages, Has.Some.Contain("類型「change」的流程裡沒有狀態「gone」").And.Contain("子項目"));
    }

    // 報告

    [Test]
    public async Task Reminder_SummarisesTheCount_InOneNote()
    {
        // Arrange
        var dump = Dump(Item("1", status: null), Item("2", status: "Working on it"), Item("3", status: "Working on it", updates: RecentComment), Item("4", status: "Done"));

        // Act
        var plan = await PlanWith(_server, dump);

        // Assert
        var notes = plan.Reminders.ToList().Where(note => note.Kind == NoteKinds.Stale).ToArray();
        Assert.That(notes, Has.Length.EqualTo(1));
        Assert.That(notes[0].Message, Is.EqualTo("2 個很久沒動的項目（最後一次動早於 2026-11-02）匯成「取消」（狀態 dropped）"));
    }

    [Test]
    public async Task DryRunReport_ShowsTheStaleCount()
    {
        // Arrange
        var plan = await PlanWith(_server, Dump(Item("1", status: null)));
        var report = new ImportReport(dryRun: true, plan.Issues, plan.Reminders);
        using var output = new StringWriter();

        // Act
        await report.WriteTextAsync(output);

        // Assert
        Assert.That(output.ToString(), Does.Contain("1 個很久沒動的項目（最後一次動早於 2026-11-02）匯成「取消」"));
    }
}
