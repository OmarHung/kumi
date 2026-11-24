using System.Text.Json.Nodes;
using PmTool.Cli.Import.Monday;

namespace PmTool.Cli.Tests;

/// <summary>kumi import monday fetch／plan：選項、輸出、結束代碼、說明；指令的兩個詞名稱不影響原本的指令。</summary>
[TestFixture]
public sealed class MondayImportCommandTests
{
    private CliHarness _cli = null!;
    private FakeMondayApi _monday = null!;

    [SetUp]
    public void SetUp()
    {
        _cli = new CliHarness().LoggedInByEnvironment();
        _cli.Environment["MONDAY_API_TOKEN"] = FakeMondayApi.Token;
        _cli.Environment["MONDAY_API_URL"] = FakeMondayApi.Url;
        _monday = new FakeMondayApi();
        _monday.Items.Add(FakeMondayApi.Item("10", "Example 項目一", "group_a", new JsonArray(FakeMondayApi.Update("u1", "2026-11-03T00:00:00Z", assets: new JsonArray(_monday.Asset("501", "shot.png")))), status: "Done"));
        _monday.Items.Add(FakeMondayApi.Item("11", "Example 項目二", "group_m11", status: "Stuck"));
        _monday.Attach(_cli.Server);
    }

    [TearDown]
    public void TearDown() => _cli.Dispose();

    private string DumpPath => _cli.PathOf("dump");

    [Test]
    public async Task Fetch_WritesTheDumpPrintsASummaryAndProgressToStderr()
    {
        // Act
        var result = await _cli.RunAsync("import", "monday", "fetch", "--board", "123", "--out", DumpPath, "--files");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("看板 123（Example Cases）：2 個項目").And.Contain("檔案：下載 1"));
            Assert.That(result.Error, Does.Contain("看板 123 第 1 頁").And.Contain("使用者：3 位"));
            Assert.That(result.Out + result.Error, Does.Not.Contain(FakeMondayApi.Token).And.Not.Contain(FakeMondayApi.SecretSignature));
            Assert.That(MondayDumpReader.Load(DumpPath).Boards.Single().Items, Has.Count.EqualTo(2));
            Assert.That(_cli.Server.Requests.Where(request => request.Path == "/v2").All(request => request.Header("Authorization") == FakeMondayApi.Token), Is.True);
            Assert.That(_cli.Server.Requests.Any(request => request.Path.StartsWith("/v1", StringComparison.Ordinal)), Is.False, "fetch 不碰 kumi 的伺服器");
        }
    }

    [Test]
    public async Task Fetch_Json_PrintsTheSummaryAsJson()
    {
        // Act
        var result = await _cli.RunAsync("import", "monday", "fetch", "--board", "123", "--out", DumpPath, "--json");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(json["boards"]![0]!["items"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(json["users"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(json["files"], Is.Null);
        }
    }

    [Test]
    public async Task Fetch_WithMappingFile_OnlyAsksForTheGroupsNotSkipped()
    {
        // Arrange
        var mapping = _cli.PathOf("monday.yaml");
        await File.WriteAllTextAsync(mapping, "dump: ./dump\nboards:\n  \"123\":\n    groups:\n      group_a: { project: CASE, type: task }\n      group_m11: skip\n");

        // Act
        var result = await _cli.RunAsync("import", "monday", "fetch", "--board", "123", "--out", DumpPath, "--mapping", mapping);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        var rules = _monday.Variables.Single(variables => variables["query"] is not null)["query"]!["rules"]![0]!["compare_value"]!.AsArray();
        Assert.That(rules.Select(value => value!.GetValue<string>()), Is.EqualTo(new[] { "group_a" }));
    }

    [Test]
    public async Task Fetch_NoToken_ExplainsHowToSetItAndDoesNotCallMonday()
    {
        // Arrange
        _cli.Environment.Remove("MONDAY_API_TOKEN");

        // Act
        var result = await _cli.RunAsync("import", "monday", "fetch", "--board", "123", "--out", DumpPath);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("MONDAY_API_TOKEN").And.Contain("export"));
        Assert.That(_cli.Server.Requests, Is.Empty);
        Assert.That(Directory.Exists(DumpPath), Is.False);
    }

    [TestCase("--board", "abc", "--out", "x")]
    [TestCase("--out", "x")]
    [TestCase("--board", "123")]
    public async Task Fetch_BadOptions_ExitWithUsageError(params string[] options)
    {
        // Act
        var result = await _cli.RunAsync(["import", "monday", "fetch", .. options]);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(_cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task Fetch_SeveralBoards_AreAllFetched()
    {
        // Act
        var result = await _cli.RunAsync("import", "monday", "fetch", "--board", "123", "--board", "123", "--out", DumpPath);

        // Assert
        var meta = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(DumpPath, "meta.json")))!;
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(meta["boards"]!.AsArray(), Has.Count.EqualTo(1), "重複的編號只抓一次");
    }

    [Test]
    public async Task Plan_WritesTheDraftMatchingPeopleByEmailAndRefusesToOverwrite()
    {
        // Arrange
        await _cli.RunAsync("import", "monday", "fetch", "--board", "123", "--out", DumpPath);
        _cli.Server.On("GET", "/v1/people", new[]
        {
            new { personId = "p1", email = "pm@example.com", displayName = "Example PM" },
            new { personId = "p2", email = "other@example.com", displayName = "Other" },
        });
        var output = _cli.PathOf("out/monday.yaml");

        // Act
        var first = await _cli.RunAsync("import", "monday", "plan", "--dump", DumpPath, "--out", output);
        var second = await _cli.RunAsync("import", "monday", "plan", "--dump", DumpPath, "--out", output);
        var forced = await _cli.RunAsync("import", "monday", "plan", "--dump", DumpPath, "--out", output, "--force", "--json");

        // Assert
        var loaded = MondayMappingFile.Load(output);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExitCode, Is.EqualTo(ExitCodes.Success), first.Error);
            Assert.That(first.Out, Does.Contain("依 Email 對上 1 位"));
            Assert.That(second.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(second.Error, Does.Contain("--force"));
            Assert.That(forced.ExitCode, Is.EqualTo(ExitCodes.Success), forced.Error);
            Assert.That(JsonNode.Parse(forced.Out)!["people"]!["matched"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(loaded.Dump, Is.EqualTo(Path.GetFullPath(DumpPath)), "對應檔裡存相對路徑，讀回來是同一個資料夾");
            Assert.That(await File.ReadAllTextAsync(output), Does.Contain("dump: \"../dump\""));
            Assert.That(loaded.People["1001"], Is.EqualTo("pm@example.com"));
            Assert.That(loaded.People["1002"], Is.Null, "Dev@Example.com 在這裡沒有人");
            Assert.That(loaded.Boards["123"].StatusColumn, Is.EqualTo("status"));
            Assert.That(loaded.Boards["123"].Groups["group_m11"].Cycle!.Name, Is.EqualTo("2026 年 11 月"));
        }
    }

    [Test]
    public async Task Plan_DumpMissing_SaysToRunFetch()
    {
        // Act
        var result = await _cli.RunAsync("import", "monday", "plan", "--dump", _cli.PathOf("nothing"), "--out", _cli.PathOf("m.yaml"));

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("kumi import monday fetch"));
    }

    [Test]
    public async Task Help_ListsTheImportCommands_AndTheGroupCanBeReachedStepByStep()
    {
        // Act
        var overview = await _cli.RunAsync("help");
        var group = await _cli.RunAsync("import", "--help");
        var half = await _cli.RunAsync("import", "monday");
        var command = await _cli.RunAsync("import", "monday", "fetch", "--help");
        var unknown = await _cli.RunAsync("import", "nope");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overview.Out, Does.Contain("import monday fetch").And.Contain("import monday plan"));
            Assert.That(group.Out, Does.Contain("import monday fetch"));
            Assert.That(half.Out, Does.Contain("import monday plan"));
            Assert.That(command.Out, Does.Contain("--board").And.Contain("--files").And.Contain("--mapping").And.Contain("--out"));
            Assert.That(command.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(unknown.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(unknown.Error, Does.Contain("沒有 nope"));
        }
    }
}
