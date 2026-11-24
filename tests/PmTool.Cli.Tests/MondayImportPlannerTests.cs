using System.Net;
using PmTool.Cli.Import.Monday;
using PmTool.Cli.Import.Monday.Run;
using static PmTool.Cli.Tests.ImportFixtures;

namespace PmTool.Cli.Tests;

/// <summary>匯入的檢查與計畫：項目轉成哪一列、負責人與關注的人、欄位、檢查階段會列出的問題（交接第 6.1～6.3 節）。</summary>
[TestFixture]
public sealed class MondayImportPlannerTests
{
    private FakeServer _server = null!;
    private FakePmBackend _backend = null!;

    [SetUp]
    public void SetUp() => _server = Server(out _backend);

    [TearDown]
    public void TearDown() => _server.Dispose();

    private static string[] Messages(Notes notes) => [.. notes.ToList().Select(note => note.Message)];

    private async Task<ImportPlan> PlanAsync(MondayDump dump, MondayMapping? mapping = null, bool addMembers = false) =>
        await ImportFixtures.PlanAsync(_server, dump, mapping ?? Mapping(), addMembers);


    [Test]
    public async Task Row_CarriesTheCreatorTimeStateAndTheMondayLink_ButNoDescription()
    {
        // Arrange
        var item = Item("10", "\uFEFF Example 項目\u200B ", status: "Working on it");
        var mapping = Mapping(board => board with { LinkField = "monday_url" });

        // Act
        var plan = await PlanAsync(Dump(item), mapping);

        // Assert
        var row = plan.Projects["CASE"].Items.Single().Row;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
            Assert.That(row["externalId"]!.GetValue<string>(), Is.EqualTo("10"));
            Assert.That(row["type"]!.GetValue<string>(), Is.EqualTo("task"));
            Assert.That(row["title"]!.GetValue<string>(), Is.EqualTo("Example 項目"));
            Assert.That(row["createdBy"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Pm.ToString()));
            Assert.That(DateTimeOffset.Parse(row["createdAt"]!.GetValue<string>()), Is.EqualTo(Created));
            Assert.That(row["state"]!.GetValue<string>(), Is.EqualTo("doing"));
            Assert.That(row["fields"]!["monday_url"]!.GetValue<string>(), Is.EqualTo("https://example.monday.test/boards/1/pulses/10"));
            Assert.That(row.ContainsKey("description"), Is.False);
            Assert.That(row.ContainsKey("parentExternalId"), Is.False);
        }
    }

    [Test]
    public async Task Row_EmptyStatusLabel_UsesTheEmptyLabelMapping_AndPriorityAndDatesAreConverted()
    {
        // Arrange
        var item = Item("10", status: null, extra:
        [
            Status("priority", "High"), Date("date4", "2026-11-20"), Timeline("timeline", "2026-11-05", "2026-11-30"),
        ]);

        // Act
        var plan = await PlanAsync(Dump(item));

        // Assert
        var row = plan.Projects["CASE"].Items.Single().Row;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row["state"]!.GetValue<string>(), Is.EqualTo("todo"));
            Assert.That(row["priority"]!.GetValue<string>(), Is.EqualTo("high"));
            Assert.That(row["startDate"]!.GetValue<string>(), Is.EqualTo("2026-11-05"));
            Assert.That(row["dueDate"]!.GetValue<string>(), Is.EqualTo("2026-11-20"), "到期日欄優先於 timeline 的 to");
        }
    }

    [Test]
    public async Task Row_WithoutADueColumnValue_UsesTheTimelineEnd_AndDropsAStartAfterTheDue()
    {
        // Arrange
        var timelineOnly = Item("10", extra: [Timeline("timeline", "2026-11-05", "2026-11-30")]);
        var backwards = Item("11", extra: [Date("date4", "2026-11-01"), Timeline("timeline", "2026-11-05", "2026-11-30")]);

        // Act
        var plan = await PlanAsync(Dump(timelineOnly, backwards));

        // Assert
        var rows = plan.Projects["CASE"].Items.Select(item => item.Row).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows[0]["dueDate"]!.GetValue<string>(), Is.EqualTo("2026-11-30"));
            Assert.That(rows[1]["dueDate"]!.GetValue<string>(), Is.EqualTo("2026-11-01"));
            Assert.That(rows[1].ContainsKey("startDate"), Is.False);
            Assert.That(Messages(plan.Reminders), Has.Some.Contain("開始日比到期日晚"));
        }
    }

    [Test]
    public async Task Row_PriorityWithoutAMapping_IsNone_AndRemindsOnce()
    {
        // Arrange
        var items = new[] { Item("10", extra: [Status("priority", "Critical")]), Item("11", extra: [Status("priority", "Critical")]) };

        // Act
        var plan = await PlanAsync(Dump(items));

        // Assert
        var reminder = plan.Reminders.ToList().Single(note => note.Kind == NoteKinds.Priority);
        Assert.That(plan.Projects["CASE"].Items.All(item => !item.Row.ContainsKey("priority")), Is.True);
        Assert.That((reminder.Message, reminder.Count), Is.EqualTo(("優先度標籤「Critical」沒有對應，當成 none", 2)));
    }

    [Test]
    public async Task Title_IsTruncatedTo300_AndAnEmptyOneGetsAPlaceholder()
    {
        // Arrange
        var items = new[] { Item("10", new string('字', 301)), Item("11", "\uFEFF  ") };

        // Act
        var plan = await PlanAsync(Dump(items));

        // Assert
        var rows = plan.Projects["CASE"].Items.Select(item => item.Row).ToArray();
        Assert.That(rows[0]["title"]!.GetValue<string>(), Has.Length.EqualTo(300));
        Assert.That(rows[1]["title"]!.GetValue<string>(), Is.EqualTo("（無標題）"));
        Assert.That(plan.Reminders.ToList().Count(note => note.Kind == NoteKinds.Title), Is.EqualTo(2));
    }

    [Test]
    public async Task Assignee_IsTheOwnerWithTheMostComments_ExcludingTheExcluded_OthersAndCommentersFollow()
    {
        // Arrange
        var item = Item(
            "10", owners: ["1001", "1002"], updates: [Update("u1", "1002"), Update("u2", "1001", at: "2026-11-02T05:00:00Z"), Update("u3", "1001", at: "2026-11-02T06:00:00Z")]);

        // Act
        var plan = await PlanAsync(Dump(item));

        // Assert
        var row = plan.Projects["CASE"].Items.Single().Row;
        Assert.That(row["assigneeId"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Dev.ToString()), "PM 在 exclude 裡，留言再多也不是負責人");
        Assert.That(row["followers"]!.AsArray().Select(id => id!.GetValue<string>()), Is.EqualTo(new[] { FakePmBackend.Pm.ToString() }));
    }

    [Test]
    public async Task Assignee_WhenOnlyTheExcludedPersonIsLeft_StillUsesThem()
    {
        // Arrange
        var item = Item("10", owners: ["1001"]);

        // Act
        var plan = await PlanAsync(Dump(item));

        // Assert
        Assert.That(plan.Projects["CASE"].Items.Single().Row["assigneeId"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Pm.ToString()));
    }

    [Test]
    public async Task Assignee_WithEqualCommentCounts_TakesTheFirstInTheColumn()
    {
        // Arrange
        var mapping = Mapping(change: mapping => mapping with { Assignee = new AssigneeRule() });
        var item = Item("10", owners: ["1002", "1001"], updates: [Update("u1", "1001"), Update("u2", "1002", at: "2026-11-02T05:00:00Z")]);

        // Act
        var plan = await PlanAsync(Dump(item), mapping);

        // Assert
        Assert.That(plan.Projects["CASE"].Items.Single().Row["assigneeId"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Dev.ToString()));
    }

    [Test]
    public async Task Followers_IncludeCommentersWhoAreMatched_ButNotFormerStaff_AndStopWhenFollowCommentersIsOff()
    {
        // Arrange
        var item = Item("10", owners: ["1001"], updates: [Update("u1", "1002", replies: [Reply("r1", "1003")])]);

        // Act
        var plan = await PlanAsync(Dump(item));
        var without = await ImportFixtures.PlanAsync(_server, Dump(item), Mapping(change: mapping => mapping with { FollowCommenters = false }));

        // Assert
        Assert.That(plan.Projects["CASE"].Items.Single().Row["followers"]!.AsArray().Select(id => id!.GetValue<string>()), Is.EqualTo(new[] { FakePmBackend.Dev.ToString() }));
        Assert.That(without.Projects["CASE"].Items.Single().Row.ContainsKey("followers"), Is.False);
    }

    [Test]
    public async Task Owners_WhoAreNotProjectMembers_AreSkipped_UnlessMembersWillBeAdded()
    {
        // Arrange
        _backend.Members["CASE"].Remove(FakePmBackend.Dev);
        var item = Item("10", owners: ["1002"]);

        // Act
        var plan = await PlanAsync(Dump(item));
        var adding = await PlanAsync(Dump(item), addMembers: true);

        // Assert
        Assert.That(plan.Projects["CASE"].Items.Single().Row.ContainsKey("assigneeId"), Is.False);
        Assert.That(Messages(plan.Reminders), Has.Some.Contain("對不上這裡的專案成員"));
        Assert.That(adding.Projects["CASE"].Items.Single().Row["assigneeId"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Dev.ToString()));
        Assert.That(adding.Projects["CASE"].MissingMembers, Is.EqualTo(new[] { FakePmBackend.Dev }));
    }

    [Test]
    public async Task Filter_DeletedItemsAreNeverImported_ArchivedOnlyWhenAsked_AndSkippedGroupsNever()
    {
        // Arrange
        var dump = Dump(Item("1"), Item("2", state: "deleted"), Item("3", state: "archived"), Item("4", group: "g3"), Item("5", group: "g9"));

        // Act
        var plan = await PlanAsync(dump);
        var withArchived = await ImportFixtures.PlanAsync(_server, dump, Mapping(change: mapping => mapping with { IncludeArchived = true }));

        // Assert
        Assert.That(plan.Projects["CASE"].Items.Select(item => item.ExternalId), Is.EqualTo(new[] { "1" }));
        Assert.That(withArchived.Projects["CASE"].Items.Select(item => item.ExternalId), Is.EqualTo(new[] { "1", "3" }));
        Assert.That(Messages(plan.Reminders), Has.Some.Contain("有 1 個群組沒有匯入").And.Some.Contain("「g9」（1）"));
    }

    [Test]
    public async Task Subitems_AreImportedAfterTheirParent_WithTheSubitemType_OnlyWhenTheMappingHasSubitems()
    {
        // Arrange
        var subitem = Item("20", "子項目", creator: "1002", status: "Done", owners: ["1002"]);
        var parent = Item("10", subitems: [subitem, Item("21", state: "deleted")]);
        var mapping = Mapping(board => board with { Subitems = new SubitemMapping { Type = "change", StatusColumn = "status", OwnerColumn = "person" } });

        // Act
        var plan = await PlanAsync(Dump(parent), mapping);
        var without = await PlanAsync(Dump(parent));

        // Assert
        var items = plan.Projects["CASE"].Items;
        Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
        Assert.That(items.Select(item => item.ExternalId), Is.EqualTo(new[] { "10", "20" }));
        Assert.That(items[1].Row["parentExternalId"]!.GetValue<string>(), Is.EqualTo("10"));
        Assert.That(items[1].Row["type"]!.GetValue<string>(), Is.EqualTo("change"));
        Assert.That(items[1].Row["state"]!.GetValue<string>(), Is.EqualTo("done"));
        Assert.That(items[1].Row["createdBy"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Dev.ToString()));
        Assert.That(without.Projects["CASE"].Items.Select(item => item.ExternalId), Is.EqualTo(new[] { "10" }));
    }

    [Test]
    public async Task Subitems_TypeIsOnlyCheckedForGroupsThatHaveSubitems()
    {
        // Arrange：子項目的類型 nope 哪個專案都沒有；g1 的項目有子項目，g2（MNT）的沒有
        var parent = Item("10", subitems: [Item("20", "子項目", status: "Done")]);
        var mapping = Mapping(board => board with { Subitems = new SubitemMapping { Type = "nope", StatusColumn = "status" } });

        // Act
        var plan = await PlanAsync(Dump(parent, Item("2", group: "g2")), mapping);

        // Assert
        var messages = Messages(plan.Issues);
        Assert.That(messages, Has.Some.Contain("專案 CASE 沒有項目類型「nope」"));
        Assert.That(messages, Has.None.Contain("專案 MNT"), string.Join("\n", messages));
    }

    [Test]
    public async Task Groups_CarryTheirProjectCycleAndOwnStates()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            Groups = new Dictionary<string, GroupMapping>
            {
                ["g1"] = new() { Project = "CASE", Type = "task", States = new Dictionary<string, string> { ["Done"] = "doing" } },
                ["g2"] = board.Groups["g2"],
            },
        });
        var dump = Dump(Item("1"), Item("2", group: "g2", status: "Working on it"));

        // Act
        var plan = await PlanAsync(dump, mapping);

        // Assert
        Assert.That(plan.Projects.Keys, Is.EqualTo(new[] { "CASE", "MNT" }));
        Assert.That(plan.Projects["CASE"].Items.Single().Row["state"]!.GetValue<string>(), Is.EqualTo("doing"), "群組的狀態對應蓋過看板的");
        var maintenance = plan.Projects["MNT"];
        Assert.That(maintenance.Items.Single().CycleName, Is.EqualTo("2026 年 11 月"));
        Assert.That(maintenance.Cycles.Keys, Is.EqualTo(new[] { "2026 年 11 月" }));
    }

    [Test]
    public async Task People_FormerStaffUseTheFallbackPerson_AndTheyAreRequiredMembers()
    {
        // Arrange
        var item = Item("10", creator: "1003", updates: [Update("u1", "1003")]);

        // Act
        var plan = await PlanAsync(Dump(item));

        // Assert
        var planned = plan.Projects["CASE"].Items.Single();
        Assert.That(planned.Creator.UsedFallback, Is.True);
        Assert.That(planned.Row["createdBy"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Former.ToString()));
        Assert.That(Messages(plan.Reminders), Has.Some.Contain("Former Staff 對不上這裡的人，建立的項目改用 fallbackPerson"));
        Assert.That(plan.Projects["CASE"].Required, Does.Contain(FakePmBackend.Former));
    }

    [Test]
    public async Task Issues_UnmatchedPeopleWithoutAFallbackPerson_AreListedOncePerPerson()
    {
        // Arrange
        var mapping = Mapping(change: mapping => mapping with { FallbackPerson = null });
        var dump = Dump(Item("10", creator: "1003", updates: [Update("u1", "1003"), Update("u2", "1003")]), Item("11", creator: null));

        // Act
        var plan = await PlanAsync(dump, mapping);

        // Assert
        var notes = plan.Issues.ToList().Where(note => note.Kind == NoteKinds.Person).ToArray();
        Assert.That(notes, Has.Length.EqualTo(2));
        Assert.That(notes[0].Message, Does.Contain("Former Staff").And.Contain("也沒有設定 fallbackPerson"));
        Assert.That(notes[0].Count, Is.EqualTo(3));
        Assert.That(notes[1].Message, Does.Contain("不明的人"));
    }

    [Test]
    public async Task Issues_MappedEmailMissingOnTheServer_AndFallbackPersonMissing_AreListed()
    {
        // Arrange
        var mapping = Mapping(change: mapping => mapping with
        {
            People = new Dictionary<string, string?> { ["1001"] = "nobody@example.com", ["1002"] = "dev@example.com" },
            FallbackPerson = "gone@example.com",
        });

        // Act
        var plan = await PlanAsync(Dump(Item("10")), mapping);

        // Assert
        Assert.That(Messages(plan.Issues), Has.Some.Contain("fallbackPerson gone@example.com 在這個伺服器上找不到")
            .And.Some.Contain("nobody@example.com 在這個伺服器上找不到"));
    }

    [Test]
    public async Task Issues_MissingProjectMembers_AreListedUnlessAddMembersIsOn()
    {
        // Arrange
        _backend.Members["CASE"].Remove(FakePmBackend.Pm);
        _backend.Members["CASE"].Remove(FakePmBackend.Former);

        // Act
        var plan = await PlanAsync(Dump(Item("10", updates: [Update("u1", "1003")])));
        var adding = await PlanAsync(Dump(Item("10", updates: [Update("u1", "1003")])), addMembers: true);

        // Assert
        Assert.That(Messages(plan.Issues), Has.Some.Contain("專案 CASE 要有這些人當成員：").And.Some.Contain("Example PM").And.Some.Contain("Former Staff"));
        Assert.That(adding.Issues.Any, Is.False);
        Assert.That(adding.Projects["CASE"].MissingMembers, Is.EquivalentTo(new[] { FakePmBackend.Pm, FakePmBackend.Former }));
    }

    [Test]
    public async Task Issues_ProjectAndTypeProblems_AreAllListedTogether()
    {
        // Arrange
        _backend.ArchivedProjects.Add("MNT");
        _backend.CanManage = false;
        _server.OnError("GET", "/v1/projects/ZZZ", HttpStatusCode.NotFound, "project.not_found", "找不到專案 ZZZ。");
        var mapping = Mapping(board => board with
        {
            Groups = new Dictionary<string, GroupMapping>
            {
                ["g1"] = new() { Project = "CASE", Type = "nope" },
                ["g2"] = new() { Project = "MNT", Type = "change" },
                ["g3"] = new() { Project = "ZZZ", Type = "task" },
            },
        });

        // Act
        var plan = await PlanAsync(Dump(Item("1"), Item("2", group: "g2"), Item("3", group: "g3")), mapping);

        // Assert
        var messages = Messages(plan.Issues);
        Assert.That(messages, Has.Some.Contain("專案 CASE 沒有項目類型「nope」"));
        // 封存的專案不在這裡擋：重跑時沒有新東西就略過，有新東西才在寫入之前停下（MondayRunCommandTests）
        Assert.That(messages, Has.None.Contain("已經封存"));
        Assert.That(messages, Has.Some.Contain("專案 MNT").And.Some.Contain("沒有 project.manage 權限"));
        Assert.That(messages, Has.Some.Contain("專案 ZZZ：找不到專案 ZZZ。"));
    }

    [Test]
    public async Task Issues_UnmappedStatusLabels_AndStatesOutsideTheWorkflow_AreCounted()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            States = new Dictionary<string, string> { ["Done"] = "released", ["Working on it"] = "doing" },
        });
        var dump = Dump(Item("1", status: "Done"), Item("2", status: "Stuck"), Item("3", status: "Stuck"), Item("4", status: null));

        // Act
        var plan = await PlanAsync(dump, mapping);

        // Assert
        var notes = plan.Issues.ToList();
        Assert.That(notes.Single(note => note.Message.Contains("流程裡沒有狀態「released」")).Count, Is.EqualTo(1));
        var stuck = notes.Single(note => note.Message.Contains("「Stuck」沒有對應"));
        Assert.That(stuck.Count, Is.EqualTo(2));
        Assert.That(notes.Single(note => note.Message.Contains("（空白）沒有對應")).Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Issues_FieldProblems_AreListedWithTheirReason()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            LinkField = "actual_hours",
            Fields = new Dictionary<string, FieldMapping>
            {
                ["numbers7"] = new() { Field = "missing_field" },
                ["text1"] = new() { Field = "approved" },
                ["client"] = new() { Field = "client", Values = new Dictionary<string, string> { ["Example A"] = "client_z" } },
                ["drop"] = new() { Field = "tags" },
            },
        });

        // Act
        var plan = await PlanAsync(Dump(Item("1")), mapping);

        // Assert
        var messages = Messages(plan.Issues);
        Assert.That(messages, Has.Some.Contain("欄位「missing_field」在所有目標（CASE 的類型「task」）都沒有"));
        Assert.That(messages, Has.Some.Contain("欄位「approved」（checkbox）收不下 monday 的欄「Memo」（long_text）；它收：checkbox"));
        Assert.That(messages, Has.Some.Contain("沒有選項「client_z」（對應檔把「Example A」對到它）"));
        Assert.That(messages, Has.Some.Contain("linkField「actual_hours」要是 url 欄位，它是 number"));
        Assert.That(messages.Count(message => message.Contains("tags")), Is.Zero, "dropdown 收進多選，沒有問題");
    }

    [Test]
    public async Task Issues_FieldNotAttachedToTheType_IsListed()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            Groups = new Dictionary<string, GroupMapping> { ["g1"] = new() { Project = "CASE", Type = "change" } },
            Fields = new Dictionary<string, FieldMapping> { ["numbers7"] = new() { Field = "actual_hours" } },
        });

        // Act
        var plan = await PlanAsync(Dump(Item("1")), mapping);

        // Assert
        Assert.That(Messages(plan.Issues), Has.Some.Contain("欄位「actual_hours」在所有目標（CASE 的類型「change」）都沒有"));
    }

    [Test]
    public async Task Issues_MappingAgainstTheDump_AreListed()
    {
        // Arrange
        var mapping = Mapping(
            board => board with
            {
                StatusColumn = "nope",
                OwnerColumn = "status",
                Priorities = new Dictionary<string, string> { ["High"] = "huge" },
                Subitems = new SubitemMapping { Type = "task" },
                Groups = new Dictionary<string, GroupMapping>
                {
                    ["g1"] = new() { Project = "CASE" },
                    ["g2"] = new() { Type = "change" },
                    ["g_missing"] = new() { Project = "CASE", Type = "task" },
                },
            },
            change: mapping => mapping with
            {
                Source = "Monday!",
                Boards = new Dictionary<string, BoardMapping>(mapping.Boards) { ["999"] = new() },
            });

        // Act
        var plan = await PlanAsync(Dump(Item("1")), mapping);

        // Assert
        var messages = Messages(plan.Issues);
        Assert.That(messages, Has.Some.Contain("source「Monday!」"));
        Assert.That(messages, Has.Some.Contain("對應檔的看板 999 不在 dump 裡"));
        Assert.That(messages, Has.Some.Contain("欄 nope 不在 dump 裡"));
        Assert.That(messages, Has.Some.Contain("欄 status（Status）是 status，要是 people"));
        Assert.That(messages, Has.Some.Contain("優先度「High」對應到「huge」"));
        Assert.That(messages, Has.Some.Contain("群組「Example 官網改版」（g1）有 project 沒有 type"));
        Assert.That(messages, Has.Some.Contain("群組「2026年11月例行維護」（g2）有 type 沒有 project"));
        Assert.That(messages, Has.Some.Contain("群組 g_missing 不在 dump 裡"));
    }

    [Test]
    public async Task Issues_SubitemsMappedButTheBoardHasNoSubitemColumns_AreListed()
    {
        // Arrange
        var dump = Dump(Item("1")) with { Boards = [Dump().Boards[0] with { SubitemColumns = [] }] };
        var mapping = Mapping(board => board with { Subitems = new SubitemMapping { Type = "task" } });

        // Act
        var plan = await PlanAsync(dump, mapping);

        // Assert
        Assert.That(Messages(plan.Issues), Has.Some.Contain("這個看板沒有子項目的欄"));
    }

    [Test]
    public async Task Fields_AreConvertedFromTheirColumns_AndUnusableValuesRemind()
    {
        // Arrange
        var mapping = Mapping(board => board with
        {
            Fields = new Dictionary<string, FieldMapping>
            {
                ["numbers7"] = new() { Field = "actual_hours" },
                ["text1"] = new() { Field = "notes" },
                ["client"] = new() { Field = "client", Values = new Dictionary<string, string> { ["Example, A"] = "client_a" } },
                ["drop"] = new() { Field = "tags", Values = new Dictionary<string, string> { ["x"] = "t1", ["y"] = "t2" } },
                ["check"] = new() { Field = "approved" },
                ["reviewer"] = new() { Field = "reviewer" },
                ["linkcol"] = new() { Field = "ref_url" },
            },
        });
        var item = Item("1", extra:
        [
            Text("numbers7", "numbers", "12.5"),
            Text("text1", "long_text", "備註 \uFEFF文字"),
            Relation("client", "Example, A", "Example B"),
            Text("drop", "dropdown", "y, x, z"),
            Text("check", "checkbox", null, "{\"checked\":\"true\"}"),
            People("reviewer", "1003", "1002"),
            Text("linkcol", "link", "docs", "{\"url\":\"ftp://example.com/x\",\"text\":\"docs\"}"),
        ]);

        // Act
        var plan = await PlanAsync(Dump(item), mapping);

        // Assert
        var fields = plan.Projects["CASE"].Items.Single().Row["fields"]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Issues.Any, Is.False, string.Join("\n", Messages(plan.Issues)));
            Assert.That(fields["actual_hours"]!.GetValue<decimal>(), Is.EqualTo(12.5m));
            Assert.That(fields["notes"]!.GetValue<string>(), Is.EqualTo("備註 文字"));
            Assert.That(fields["client"]!.GetValue<string>(), Is.EqualTo("client_a"));
            Assert.That(fields["tags"]!.AsArray().Select(tag => tag!.GetValue<string>()), Is.EqualTo(new[] { "t2", "t1" }));
            Assert.That(fields["approved"]!.GetValue<bool>(), Is.True);
            Assert.That(fields["reviewer"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Dev.ToString()), "1003 對不上，取第一個對得上的 1002");
            Assert.That(fields.AsObject().ContainsKey("ref_url"), Is.False);
            Assert.That(Messages(plan.Reminders), Has.Some.Contain("欄位「ref_url」的值「ftp://example.com/x」不是 http 或 https 網址"));
        }
    }
}
