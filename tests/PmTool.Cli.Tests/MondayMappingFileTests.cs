using PmTool.Cli.Import.Monday;

namespace PmTool.Cli.Tests;

/// <summary>對應檔的 YAML：交接文件的範例讀得進來、簡寫、寫了再讀回來一樣、錯誤帶行號。</summary>
[TestFixture]
public sealed class MondayMappingFileTests
{
    private const string SpecExample = """
        # monday 匯入的對應
        source: monday
        dump: ./monday-dump
        people:
          "1001": pm@example.com         # 範例 PM
          "1002": dev@example.com
          "1003": null                   # 前同事
        fallbackPerson: former@example.com
        assignee:
          exclude: ["1001"]
        followCommenters: true
        includeArchived: false
        boards:
          "123":
            statusColumn: status8
            ownerColumn: person
            dueColumn: date5
            timelineColumn: timeline
            priorityColumn: status0
            priorities: { High: high, Medium: medium, Low: low }
            states:
              "Done": done
              "Working on it": doing
              "": todo
            fields:
              numbers7: actual_hours
              board_relation_x:
                field: client
                values: { "Example 正式站": client_a, "Example 測試站": client_a }
            linkField: monday_url
            subitems:
              type: task
              statusColumn: status
              ownerColumn: person
            groups:
              group_m11:
                project: MNT
                type: change
                cycle: { name: "2026 年 11 月", start: 2026-11-01, end: 2026-11-30 }
              group_case1:
                project: CASE
                type: task
                states: { "Done": done, "Working on it": doing, "": todo }
              group_old: skip
        """;

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"pm-mapping-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private string Write(string yaml, string name = "monday.yaml")
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, yaml);
        return path;
    }

    [Test]
    public void Load_TheHandoffExample_ReadsEverythingIncludingShorthands()
    {
        // Arrange
        var path = Write(SpecExample);

        // Act
        var mapping = MondayMappingFile.Load(path);

        // Assert
        var board = mapping.Boards["123"];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.Source, Is.EqualTo("monday"));
            Assert.That(mapping.Dump, Is.EqualTo(Path.GetFullPath(Path.Combine(_directory, "monday-dump"))), "相對路徑以對應檔所在的資料夾為準");
            Assert.That(mapping.People["1001"], Is.EqualTo("pm@example.com"));
            Assert.That(mapping.People["1003"], Is.Null);
            Assert.That(mapping.FallbackPerson, Is.EqualTo("former@example.com"));
            Assert.That(mapping.Assignee.Exclude, Is.EqualTo(new[] { "1001" }));
            Assert.That(mapping.FollowCommenters, Is.True);
            Assert.That(mapping.IncludeArchived, Is.False);
            Assert.That(board.StatusColumn, Is.EqualTo("status8"));
            Assert.That(board.PriorityColumn, Is.EqualTo("status0"));
            Assert.That(board.Priorities["High"], Is.EqualTo("high"));
            Assert.That(board.States, Is.EqualTo(new Dictionary<string, string> { ["Done"] = "done", ["Working on it"] = "doing", [""] = "todo" }));
            Assert.That(board.Fields["numbers7"].Field, Is.EqualTo("actual_hours"), "欄位簡寫");
            Assert.That(board.Fields["numbers7"].Values, Is.Empty);
            Assert.That(board.Fields["board_relation_x"].Values["Example 測試站"], Is.EqualTo("client_a"));
            Assert.That(board.LinkField, Is.EqualTo("monday_url"));
            Assert.That(board.Subitems!.Type, Is.EqualTo("task"));
            Assert.That(board.Subitems.States, Is.Null);
            Assert.That(board.Groups["group_old"].Skip, Is.True, "群組簡寫");
            Assert.That(board.Groups["group_m11"].Cycle, Is.EqualTo(new CycleMapping("2026 年 11 月", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30))));
            Assert.That(board.Groups["group_case1"].States!["Done"], Is.EqualTo("done"));
            Assert.That(board.Groups["group_case1"].Project, Is.EqualTo("CASE"));
        }
    }

    [Test]
    public void Load_AbsoluteDump_IsKept()
    {
        // Arrange
        var path = Write("dump: /srv/example/dump\n");

        // Act
        var mapping = MondayMappingFile.Load(path);

        // Assert
        Assert.That(mapping.Dump, Is.EqualTo("/srv/example/dump"));
        Assert.That(mapping.Source, Is.EqualTo("monday"));
        Assert.That(mapping.FollowCommenters, Is.True);
    }

    [Test]
    public void SaveThenLoad_GivesTheSameMapping()
    {
        // Arrange
        var original = MondayMappingFile.Load(Write(SpecExample));
        var copy = Path.Combine(_directory, "copy.yaml");

        // Act
        MondayMappingFile.Save(copy, original);
        var again = MondayMappingFile.Load(copy);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Dump, Is.EqualTo(original.Dump));
            Assert.That(again.People, Is.EqualTo(original.People));
            Assert.That(again.FallbackPerson, Is.EqualTo(original.FallbackPerson));
            Assert.That(again.Assignee.Exclude, Is.EqualTo(original.Assignee.Exclude));
            Assert.That(again.Boards["123"].States, Is.EqualTo(original.Boards["123"].States));
            Assert.That(again.Boards["123"].Fields["board_relation_x"].Values, Is.EqualTo(original.Boards["123"].Fields["board_relation_x"].Values));
            Assert.That(again.Boards["123"].Fields["numbers7"].Field, Is.EqualTo("actual_hours"));
            Assert.That(again.Boards["123"].Groups["group_m11"].Cycle, Is.EqualTo(original.Boards["123"].Groups["group_m11"].Cycle));
            Assert.That(again.Boards["123"].Groups["group_old"].Skip, Is.True);
            Assert.That(again.Boards["123"].Groups["group_case1"].States, Is.EqualTo(original.Boards["123"].Groups["group_case1"].States));
            Assert.That(again.Boards["123"].Subitems!.Type, Is.EqualTo("task"));
        }
    }

    [Test]
    public void Save_TrickyStrings_StayStringsWhenReadBack()
    {
        // Arrange
        var tricky = new[] { "yes", "null", "1001", "a: b", "# not a comment", "quote\"and\\slash", "line\nbreak", "  spaced  ", "- dash", "Example 正式站", "" };
        var mapping = new MondayMapping
        {
            Dump = "./dump",
            Boards = new Dictionary<string, BoardMapping>
            {
                ["1"] = new() { States = tricky.Where(label => label.Length > 0).ToDictionary(label => label, label => "state_" + label.Length) },
            },
        };
        var path = Path.Combine(_directory, "tricky.yaml");

        // Act
        MondayMappingFile.Save(path, mapping);
        var again = MondayMappingFile.Load(path);

        // Assert
        Assert.That(again.Boards["1"].States, Is.EqualTo(mapping.Boards["1"].States));
    }

    [Test]
    public void Save_AnnotationsWithNewlines_CannotBecomeConfiguration()
    {
        // Arrange
        var mapping = new MondayMapping
        {
            Dump = "./dump",
            Boards = new Dictionary<string, BoardMapping> { ["1"] = new() { Groups = new Dictionary<string, GroupMapping> { ["g"] = new() } } },
        };
        var notes = new MappingAnnotations();
        notes.Note("boards/1/groups/g", "Evil\nincludeArchived: true\r\n  fallbackPerson: x@example.com");
        notes.Before("boards/1/groups/g", "line1\nsource: hacked");
        var path = Path.Combine(_directory, "notes.yaml");

        // Act
        MondayMappingFile.Save(path, mapping, notes);
        var again = MondayMappingFile.Load(path);

        // Assert
        Assert.That(again.IncludeArchived, Is.False);
        Assert.That(again.FallbackPerson, Is.Null);
        Assert.That(again.Source, Is.EqualTo("monday"));
    }

    [Test]
    public void Load_BlankValues_AreTreatedAsNotFilledIn()
    {
        // Arrange
        var path = Write("""
            dump: ./d
            boards:
              "1":
                statusColumn:
                states:
                  "Done":
                  "Stuck": blocked
                fields:
                  numbers7:
                groups:
                  g1:
                    project:
                    type:
                  g2:
            """);

        // Act
        var board = MondayMappingFile.Load(path).Boards["1"];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(board.StatusColumn, Is.Null);
            Assert.That(board.States, Is.EqualTo(new Dictionary<string, string> { ["Stuck"] = "blocked" }));
            Assert.That(board.Fields, Is.Empty);
            Assert.That(board.Groups["g1"].Project, Is.Null);
            Assert.That(board.Groups["g2"].Skip, Is.False);
        }
    }

    [TestCase("dump: ./d\nbords: {}\n", 2, "不認得的鍵「bords」")]
    [TestCase("dump: ./d\nboards:\n  \"1\":\n    statusColum: x\n", 4, "statusColum")]
    [TestCase("boards: {}\n", 1, "缺少 dump")]
    [TestCase("dump: ./d\nboards:\n  \"1\":\n    groups:\n      g1:\n        cycle: { name: x, start: 2026-13-01, end: 2026-11-30 }\n", 6, "不是日期")]
    [TestCase("dump: ./d\nboards:\n  \"1\":\n    groups:\n      g1:\n        cycle: { name: x, start: 2026-11-30, end: 2026-11-01 }\n", 6, "不能早於")]
    [TestCase("dump: ./d\nboards:\n  \"1\":\n    groups:\n      g1: maybe\n", 5, "只能寫 skip")]
    [TestCase("dump: ./d\nfollowCommenters: maybe\n", 2, "true 或 false")]
    [TestCase("dump: ./d\nboards:\n  \"1\":\n    states: [a, b]\n", 4, "對應表")]
    [TestCase("dump: ./d\nboards:\n  \"1\":\n    statusColumn: [a]\n", 4, "要是一個文字")]
    [TestCase("dump: ./d\nassignee:\n  exclude: \"1001\"\n", 3, "清單")]
    [TestCase("dump: ./d\nboards:\n  \"1\":\n    subitems:\n      statusColumn: s\n", 5, "缺少 type")]
    [TestCase("dump: ./d\nboards:\n  \"1\":\n    fields:\n      c1:\n        values: { a: b }\n", 6, "缺少 field")]
    [TestCase("dump: [unclosed\n", 2, "不是合法的 YAML")]
    public void Load_StructureErrors_SayWhichLineAndWhat(string yaml, int line, string expectedPart)
    {
        // Arrange
        var path = Write(yaml);

        // Act
        var failure = Assert.Throws<CliException>(() => MondayMappingFile.Load(path));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(failure.Message, Does.Contain($"第 {line} 行").And.Contain(expectedPart).And.Contain("monday.yaml"));
        }
    }

    [Test]
    public void Load_EmptyFile_SaysSo() =>
        Assert.That(Assert.Throws<CliException>(() => MondayMappingFile.Load(Write("")))!.Message, Does.Contain("是空的"));

    [Test]
    public void Load_MissingFile_SaysSo() =>
        Assert.That(Assert.Throws<CliException>(() => MondayMappingFile.Load(Path.Combine(_directory, "nope.yaml")))!.Message, Does.Contain("讀不了對應檔"));

    [Test]
    public void Load_DuplicateKeys_AreRejected()
    {
        // Arrange
        var path = Write("dump: ./a\ndump: ./b\n");

        // Act
        var failure = Assert.Throws<CliException>(() => MondayMappingFile.Load(path));

        // Assert
        Assert.That(failure!.Message, Does.Contain("monday.yaml"));
    }
}
