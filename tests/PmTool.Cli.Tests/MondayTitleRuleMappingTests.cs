using PmTool.Cli.Import.Monday;

namespace PmTool.Cli.Tests;

/// <summary>對應檔的 titleRules、群組的 fields 與 <c>cycle: monthly</c>：讀、寫、寫了再讀回來一樣、錯誤帶行號。</summary>
[TestFixture]
public sealed class MondayTitleRuleMappingTests
{
    private const string Example = """
        dump: ./d
        boards:
          "1":
            titleRules:
              - { project: MNT, match: [漏洞, 資安, CVE], type: security }
              - { project: MNT, match: [EXA], fields: { client: client_exa } }
              - { match: ["a: b"], type: task, fields: { client: client_a, tags: "t1, t2" } }
            groups:
              group_x:
                project: MNT
                type: change
                cycle: monthly
                fields: { client: client_ntinv }
              group_y:
                project: MNT
                type: change
                cycle: { name: "2026 年 11 月", start: 2026-11-01, end: 2026-11-30 }
        """;

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"pm-rules-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private string Write(string yaml)
    {
        var path = Path.Combine(_directory, "monday.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    [Test]
    public void Load_ReadsTitleRulesGroupFieldsAndMonthlyCycle()
    {
        // Act
        var board = MondayMappingFile.Load(Write(Example)).Boards["1"];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(board.TitleRules, Has.Count.EqualTo(3));
            Assert.That(board.TitleRules[0].Project, Is.EqualTo("MNT"));
            Assert.That(board.TitleRules[0].Match, Is.EqualTo(new[] { "漏洞", "資安", "CVE" }));
            Assert.That(board.TitleRules[0].Type, Is.EqualTo("security"));
            Assert.That(board.TitleRules[0].Fields, Is.Empty);
            Assert.That(board.TitleRules[1].Type, Is.Null);
            Assert.That(board.TitleRules[1].Fields, Is.EqualTo(new Dictionary<string, string> { ["client"] = "client_exa" }));
            Assert.That(board.TitleRules[2].Project, Is.Null);
            Assert.That(board.TitleRules[2].Fields["tags"], Is.EqualTo("t1, t2"));
            Assert.That(board.Groups["group_x"].Cycle!.Monthly, Is.True);
            Assert.That(board.Groups["group_x"].Fields, Is.EqualTo(new Dictionary<string, string> { ["client"] = "client_ntinv" }));
            Assert.That(board.Groups["group_y"].Cycle!.Monthly, Is.False);
            Assert.That(board.Groups["group_y"].Cycle!.Name, Is.EqualTo("2026 年 11 月"));
            Assert.That(board.Groups["group_y"].Fields, Is.Empty);
        }
    }

    [Test]
    public void SaveThenLoad_KeepsTitleRulesGroupFieldsAndMonthlyCycle()
    {
        // Arrange
        var original = MondayMappingFile.Load(Write(Example));
        var copy = Path.Combine(_directory, "copy.yaml");

        // Act
        MondayMappingFile.Save(copy, original);
        var again = MondayMappingFile.Load(copy).Boards["1"];

        // Assert
        var rules = original.Boards["1"].TitleRules;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.TitleRules, Has.Count.EqualTo(3));
            for (var index = 0; index < rules.Count; index++)
            {
                Assert.That(again.TitleRules[index].Project, Is.EqualTo(rules[index].Project));
                Assert.That(again.TitleRules[index].Match, Is.EqualTo(rules[index].Match));
                Assert.That(again.TitleRules[index].Type, Is.EqualTo(rules[index].Type));
                Assert.That(again.TitleRules[index].Fields, Is.EqualTo(rules[index].Fields));
            }

            Assert.That(again.Groups["group_x"].Cycle!.Monthly, Is.True);
            Assert.That(again.Groups["group_x"].Fields, Is.EqualTo(new Dictionary<string, string> { ["client"] = "client_ntinv" }));
            Assert.That(again.Groups["group_y"].Cycle, Is.EqualTo(original.Boards["1"].Groups["group_y"].Cycle));
        }
    }

    [Test]
    public void Save_MonthlyCycle_IsWrittenAsTheShorthand()
    {
        // Arrange
        var mapping = new MondayMapping
        {
            Dump = "./d",
            Boards = new Dictionary<string, BoardMapping>
            {
                ["1"] = new() { Groups = new Dictionary<string, GroupMapping> { ["g"] = new() { Project = "MNT", Type = "change", Cycle = CycleMapping.MonthlyByCreation } } },
            },
        };
        var path = Path.Combine(_directory, "out.yaml");

        // Act
        MondayMappingFile.Save(path, mapping);

        // Assert
        Assert.That(File.ReadAllText(path), Does.Contain("cycle: monthly"));
    }

    [Test]
    public void Load_BlankTitleRulesAndGroupFields_AreTreatedAsNotFilledIn()
    {
        // Act
        var board = MondayMappingFile.Load(Write("dump: ./d\nboards:\n  \"1\":\n    titleRules:\n    groups:\n      g:\n        project: MNT\n        type: change\n        fields:\n")).Boards["1"];

        // Assert
        Assert.That(board.TitleRules, Is.Empty);
        Assert.That(board.Groups["g"].Fields, Is.Empty);
    }

    private const string RuleHead = "dump: ./d\nboards:\n  \"1\":\n    titleRules:\n";
    private const string GroupHead = "dump: ./d\nboards:\n  \"1\":\n    groups:\n      g:\n";

    [TestCase(RuleHead + "      - { match: [], type: security }\n", 5, "不能是空的清單")]
    [TestCase(RuleHead + "      - { match: [a, \"\"], type: security }\n", 5, "不能是空的清單")]
    [TestCase(RuleHead + "      - { match: 漏洞, type: security }\n", 5, "match")]
    [TestCase(RuleHead + "      - { type: security }\n", 5, "match")]
    [TestCase(RuleHead + "      - { match: [a] }\n", 5, "type 或 fields")]
    [TestCase(RuleHead + "      - { match: [a], type: security, typ: x }\n", 5, "不認得的鍵「typ」")]
    [TestCase(RuleHead + "      - { match: [a], fields: [client] }\n", 5, "對應表")]
    [TestCase(RuleHead + "      match: [a]\n", 5, "清單")]
    [TestCase(GroupHead + "        cycle: weekly\n", 6, "只能寫 monthly")]
    [TestCase(GroupHead + "        cycle: [a]\n", 6, "對應表")]
    [TestCase(GroupHead + "        fields: [client]\n", 6, "對應表")]
    [TestCase(GroupHead + "        fields: { client: [a] }\n", 6, "要是一個文字")]
    [TestCase(GroupHead + "        feilds: { client: a }\n", 6, "不認得的鍵「feilds」")]
    public void Load_RuleErrors_SayWhichLineAndWhat(string yaml, int line, string expectedPart)
    {
        // Act
        var failure = Assert.Throws<CliException>(() => MondayMappingFile.Load(Write(yaml)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(failure.Message, Does.Contain($"第 {line} 行"));
            Assert.That(failure.Message, Does.Contain(expectedPart));
        }
    }
}
