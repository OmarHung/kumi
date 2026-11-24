using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Import.Monday;
using PmTool.Cli.Import.Monday.Run;
using static PmTool.Cli.Tests.ImportFixtures;

namespace PmTool.Cli.Tests;

/// <summary>實測與審查之後補的規則：群組留白、欄位只在部分專案有、成員角色、控制字元、程式碼逃逸、巢狀清單、跳脫、人員問題不早退…</summary>
[TestFixture]
public sealed class MondayImportFixesTests
{
    private FakeServer _server = null!;
    private FakePmBackend _backend = null!;

    [SetUp]
    public void SetUp() => _server = Server(out _backend);

    [TearDown]
    public void TearDown() => _server.Dispose();

    private static string[] Messages(Notes notes) => [.. notes.ToList().Select(note => note.Message)];

    private static readonly HtmlContext NoPeople = new(_ => null, _ => null);

    private static string Convert(string html) => MondayHtml.ToMarkdown(html, NoPeople).Text;

    // A. 群組的 project 與 type 都留白：不匯，不是錯誤。
    [Test]
    public async Task BlankGroups_AreNotImported_AndSummarisedInOneReminderListingOnlyGroupsWithItems()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            Groups = new Dictionary<string, GroupMapping>
            {
                ["g1"] = new() { Project = "CASE", Type = "task" },
                ["g2"] = new(),
                ["g3"] = new() { Project = string.Empty, Type = " " },
            },
        });
        var dump = Dump(Item("1"), Item("2", group: "g2"), Item("3", group: "g2"), Item("4", group: "g3"));

        // Act
        var plan = await ImportFixtures.PlanAsync(_server, dump, mapping);

        // Assert
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(plan.Projects["CASE"].Items.Select(item => item.ExternalId), Is.EqualTo(new[] { "1" }));
        var reminder = plan.Reminders.ToList().Single(note => note.Kind == NoteKinds.Skipped).Message;
        Assert.That(reminder, Does.Contain("有 2 個群組沒有匯入").And.Contain("共 3 個項目").And.Contain("「2026年11月例行維護」（2）").And.Contain("「Temp」（1）"));
        Assert.That(reminder, Does.Not.Contain("Example 官網改版"));
    }

    [Test]
    public async Task BlankGroups_WithoutAnyItems_ProduceNoReminder()
    {
        // Arrange
        var mapping = Mapping(board => board with { Groups = new Dictionary<string, GroupMapping> { ["g1"] = new() { Project = "CASE", Type = "task" }, ["g2"] = new() } });

        // Act
        var plan = await ImportFixtures.PlanAsync(_server, Dump(Item("1")), mapping);

        // Assert
        Assert.That(plan.Reminders.ToList().Where(note => note.Kind == NoteKinds.Skipped), Is.Empty);
    }

    [Test]
    public async Task Group_WithOnlyOneOfProjectAndType_IsStillAnIssue()
    {
        // Arrange
        var mapping = Mapping(board => board with { Groups = new Dictionary<string, GroupMapping> { ["g2"] = new() { Type = "change" } } });

        // Act
        var plan = await ImportFixtures.PlanAsync(_server, Dump(Item("1", group: "g2")), mapping);

        // Assert
        Assert.That(Messages(plan.Issues), Has.Some.Contain("有 type 沒有 project"));
    }

    // B. 欄位只在部分目標專案有。
    [Test]
    public async Task Field_OnlyInSomeTargets_IsSkippedWithAReminderPerItem_AndIsNotAnIssue()
    {
        // Arrange
        var mapping = Mapping(board => board with { Fields = new Dictionary<string, FieldMapping> { ["numbers7"] = new() { Field = "actual_hours" } } });
        var dump = Dump(
            Item("1", extra: [Text("numbers7", "numbers", "3")]),
            Item("2", group: "g2", extra: [Text("numbers7", "numbers", "4")]),
            Item("3", group: "g2", extra: [Text("numbers7", "numbers", "5")]),
            Item("4", group: "g2"));

        // Act
        var plan = await ImportFixtures.PlanAsync(_server, dump, mapping);

        // Assert
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(plan.Projects["CASE"].Items.Single().Row["fields"]!["actual_hours"]!.GetValue<decimal>(), Is.EqualTo(3m));
        Assert.That(plan.Projects["MNT"].Items.All(item => !item.Row.ContainsKey("fields")), Is.True);
        var reminder = plan.Reminders.ToList().Single(note => note.Message.Contains("沒有欄位「actual_hours」"));
        Assert.That((reminder.Message, reminder.Count), Is.EqualTo(("專案 MNT 的類型「change」沒有欄位「actual_hours」，項目的值略過", 2)), "沒有值的項目不提醒");
    }

    // C. 成員的角色。
    [Test]
    public async Task AddMembers_RequiresTheRoleInEveryTargetBlueprint()
    {
        // Arrange
        var mapping = Mapping();

        // Act
        var plan = await ImportFixtures.PlanAsync(_server, Dump(Item("1"), Item("2", group: "g2")), mapping, addMembers: true, memberRole: "intern");
        var fine = await ImportFixtures.PlanAsync(_server, Dump(Item("1")), mapping, addMembers: true, memberRole: "viewer");
        var notAdding = await ImportFixtures.PlanAsync(_server, Dump(Item("1")), mapping, addMembers: false, memberRole: "intern");

        // Assert
        Assert.That(Messages(plan.Issues), Has.Some.Contain("專案 CASE 的藍圖裡沒有角色「intern」").And.Some.Contain("專案 MNT 的藍圖裡沒有角色「intern」"));
        Assert.That(fine.Issues.Any, Is.False);
        Assert.That(notAdding.Issues.Any, Is.False, "沒有要加成員就不檢查角色");
    }

    // 5. 人員有問題時，同一個項目的其他問題也要說。
    [Test]
    public async Task Item_WithAnUnresolvedCreator_StillReportsItsStateProblems()
    {
        // Arrange
        var mapping = Mapping(change: mapping => mapping with { FallbackPerson = null });
        var dump = Dump(Item("1", creator: "1003", status: "Stuck", extra: [Status("priority", "High")]));

        // Act
        var plan = await ImportFixtures.PlanAsync(_server, dump, mapping);

        // Assert
        Assert.That(plan.Issues.ToList().Select(note => note.Kind), Is.SupersetOf(new[] { NoteKinds.Person, NoteKinds.State }));
        Assert.That(plan.Projects["CASE"].Items, Is.Empty);
    }

    // 9. 沒寫 subitems 但有子項目。
    [Test]
    public async Task Subitems_PresentInTheDumpButNotMapped_AreCountedInAReminder()
    {
        // Arrange
        var dump = Dump(Item("1", subitems: [Item("20"), Item("21"), Item("22", state: "deleted")]), Item("2", subitems: [Item("23")]));

        // Act
        var plan = await ImportFixtures.PlanAsync(_server, dump, Mapping());

        // Assert
        var reminder = plan.Reminders.ToList().Single(note => note.Message.Contains("子項目沒有匯入"));
        Assert.That((reminder.Message, reminder.Count), Is.EqualTo(("群組 Example 官網改版（g1） 的子項目沒有匯入（對應檔沒有 subitems）", 3)));
    }

    // 10. 同名週期的日期不一致。
    [Test]
    public async Task Cycle_WithTheSameNameButDifferentDates_RemindsAndKeepsTheFirst()
    {
        // Arrange
        var cycle = (DateOnly start, DateOnly end) => new CycleMapping("2026 年 11 月", start, end);
        var mapping = Mapping(board => board with
        {
            Groups = new Dictionary<string, GroupMapping>
            {
                ["g1"] = new() { Project = "MNT", Type = "change", Cycle = cycle(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)) },
                ["g2"] = new() { Project = "MNT", Type = "change", Cycle = cycle(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 30)) },
            },
        });

        // Act
        var plan = await ImportFixtures.PlanAsync(_server, Dump(Item("1"), Item("2", group: "g2")), mapping);

        // Assert
        Assert.That(plan.Projects["MNT"].Cycles["2026 年 11 月"].Start, Is.EqualTo(new DateOnly(2026, 11, 1)));
        Assert.That(Messages(plan.Reminders), Has.Some.Contain("週期「2026 年 11 月」在對應檔裡有不同的日期"));
    }

    // 2. 控制字元。
    [Test]
    public async Task ControlCharacters_AreRemovedFromTitlesAndTextFields()
    {
        // Arrange
        var mapping = Mapping(board => board with { Fields = new Dictionary<string, FieldMapping> { ["text1"] = new() { Field = "notes" } } });
        var dump = Dump(Item("1", "標\v題\b\0", extra: [Text("text1", "long_text", "a\u0001b\tc\ud800")]));

        // Act
        var plan = await ImportFixtures.PlanAsync(_server, dump, mapping);

        // Assert
        var row = plan.Projects["CASE"].Items.Single().Row;
        Assert.That(row["title"]!.GetValue<string>(), Is.EqualTo("標題"));
        Assert.That(row["fields"]!["notes"]!.GetValue<string>(), Is.EqualTo("ab\tc"));
    }

    [Test]
    public void ControlCharacters_AreRemovedFromComments_ButNewlinesAndTabsStay()
    {
        Assert.That(Convert("<p>a\u000Bb\u0008c\u0000d</p><p>e\ud83d</p><pre>x\ty\u0007</pre>"), Is.EqualTo("abcd\n\ne\n\n```\nx\ty\n```"));
    }

    // 3. 程式碼逃逸。
    [Test]
    public void InlineCode_CannotBeEscapedByItsOwnContent()
    {
        Assert.That(Convert("<p><code>a``b @[x](mention:1)</code></p>"), Is.EqualTo("``` a``b @[x](mention:1) ```"));
    }

    [Test]
    public void CodeBlock_UsesAFenceLongerThanTheLongestBacktickRun()
    {
        Assert.That(Convert("<pre>`````\nx</pre>"), Is.EqualTo("``````\n`````\nx\n``````"));
    }

    // 4. ul 直接包 ul。
    [Test]
    public void ListInsideAListWithoutAnItem_NestsUnderThePreviousItem()
    {
        Assert.That(Convert("<ul><li>甲</li><ul><li>甲一</li></ul><li>乙</li></ul>"), Is.EqualTo("- 甲\n  - 甲一\n- 乙"));
        Assert.That(Convert("<ul><ul><li>只有內層</li></ul></ul>"), Is.EqualTo("- 只有內層"));
    }

    // 13. 跳脫的漏洞。
    [Test]
    public void TableFirstCell_EscapesLineStartMarkers()
    {
        Assert.That(Convert("<table><tr><td># 一</td><td># 二</td></tr><tr><td>- 三</td><td>x</td></tr><tr><td>&gt; 四</td></tr></table>"), Is.EqualTo("\\# 一 | # 二\n\\- 三 | x\n\\> 四"));
    }

    [Test]
    public void LineStart_EscapesSetextUnderlines()
    {
        Assert.That(Convert("<p>標題<br>===<br>---<br>--</p>"), Is.EqualTo("標題\n\\===\n\\---\n\\--"));
    }

    [Test]
    public void BareUrl_WithUnderscoresAndStars_IsWrittenInAngleBrackets()
    {
        Assert.That(Convert("<p><a href=\"https://example.com/a_b_c*d\">https://example.com/a_b_c*d</a>，麻煩依回覆</p>"), Is.EqualTo("<https://example.com/a_b_c*d>，麻煩依回覆"));
    }

    [Test]
    public void Truncate_InsideACodeFence_ClosesTheFenceBeforeTheNotice()
    {
        // Arrange
        var text = new string('字', 9_000) + "\n\n```\n" + new string('x', 2_000) + "\n```";

        // Act
        var cut = MarkdownText.Truncate(text, out var truncated);

        // Assert
        Assert.That(truncated, Is.True);
        Assert.That(cut, Does.EndWith("x\n```\n\n" + MarkdownText.TruncationNotice));
    }

    // 14. bmp 兩邊一致：不是網頁直接顯示的圖片，就當檔案。
    [Test]
    public void UnreferencedBmp_IsListedAsAFile_NotAnImage()
    {
        // Arrange
        var composer = new CommentComposer(new PersonDirectory(Mapping(), Dump(), []), new Notes());
        var comment = new FlatComment("u", "1001", Created, "<p>x</p>", [Asset("5", "scan.bmp")], null);
        var author = new ResolvedPerson(new ServerPerson(FakePmBackend.Pm, "Example PM", "pm@example.com"), false, "Example PM");
        var id = Guid.NewGuid();

        // Act
        var body = composer.Compose(comment, author, new Dictionary<string, AssetOutcome> { ["5"] = new("scan.bmp", id) }, "CASE-1");

        // Assert
        Assert.That(body, Does.EndWith($"[scan.bmp](attachment:{id})").And.Not.Contain("!["));
    }

    // 11. 數字欄先看原始數值。
    [Test]
    public void Number_PrefersTheRawValueOverTheFormattedText()
    {
        var value = new MondayColumnValue("c", "numbers", "12.5 hrs", "\"12.5\"", null, [], []);

        var result = FieldConverter.Convert("工時", "number", new FieldMapping { Field = "f" }, value, new HashSet<string>(), _ => null);

        Assert.That(result.Value!.GetValue<decimal>(), Is.EqualTo(12.5m));
    }

    // 12. 項目的檔案同時出現在留言裡，以留言為準。
    [Test]
    public void CommentThreads_FlattenKeepsRepliesRightAfterTheirParent()
    {
        var item = Item("1", updates: [Update("u2", at: "2026-11-03T00:00:00Z"), Update("u1", at: "2026-11-02T00:00:00Z", replies: [Reply("r1", at: "2026-11-09T00:00:00Z")])]);

        Assert.That(CommentThreads.Flatten(item).Select(comment => comment.Id), Is.EqualTo(new[] { "u1", "r1", "u2" }));
    }

    // 8. Retry-After 是過去的日期。
    [Test]
    public async Task RetryAfter_InThePast_WaitsZero_NotANegativeTime()
    {
        // Arrange
        var server = new FakeServer();
        var waits = new List<TimeSpan>();
        var client = new MondayGraphQlClient(new HttpClient(server), "https://monday.example.test/v2", "tok", (span, _) =>
        {
            waits.Add(span);
            return Task.CompletedTask;
        });
        var calls = 0;
        server.On("POST", "/v2", _ =>
        {
            if (++calls > 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "data": { "ok": 1 } }""") };
            }

            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddHours(-1));
            return limited;
        });

        // Act
        await client.QueryAsync(string.Empty, "ok", null, CancellationToken.None);

        // Assert
        Assert.That(waits, Is.EqualTo(new[] { TimeSpan.Zero }));
        server.Dispose();
    }

    // 15. 不認得的指令。
    [Test]
    public async Task UnknownSubcommandUnderMonday_IsAUsageError_ButThePrefixAndHelpStillListTheCommands()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();

        // Act
        var bogus = await cli.RunAsync("import", "monday", "bogus");
        var prefix = await cli.RunAsync("import", "monday");
        var help = await cli.RunAsync("import", "monday", "--help");

        // Assert
        Assert.That(bogus.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(bogus.Error, Does.Contain("沒有 bogus 這個指令"));
        Assert.That((prefix.ExitCode, help.ExitCode), Is.EqualTo((0, 0)));
        Assert.That(prefix.Out, Does.Contain("monday run"));
    }
}
