using System.Net;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Tests;

/// <summary>blueprint apply 送出成功卻套用失敗、--summary 與伺服器讀法一致、--json 不問問題，以及 validate 的訊息。</summary>
[TestFixture]
public sealed class BlueprintApplyEdgeTests
{
    private const string Preview = "/v1/projects/WEB/changesets/preview";
    private const string Propose = "/v1/projects/WEB/changesets";

    private CliHarness _cli = null!;

    [SetUp]
    public void CreateHarness() => _cli = new CliHarness().LoggedInByEnvironment();

    [TearDown]
    public void DisposeHarness() => _cli.Dispose();

    private static object Plan(object[]? impacts = null, bool canApply = true) => new
    {
        baseVersion = 1,
        currentVersion = 1,
        blueprint = new { },
        changes = new[] { new { kind = "added", resource = "state", key = "review", parentKey = "main", changedProperties = Array.Empty<string>() } },
        impacts = impacts ?? [],
        blockers = Array.Empty<object>(),
        canApply,
    };

    private string WriteFile(string name, string text)
    {
        var path = _cli.PathOf(name);
        File.WriteAllText(path, text);
        return path;
    }

    // ---- 送出成功、套用失敗 ----

    [TestCase(HttpStatusCode.Conflict, "changeset.apply_blocked", ExitCodes.NeedsDecision)]
    [TestCase(HttpStatusCode.InternalServerError, "server.error", ExitCodes.Error)]
    public async Task Apply_ProposedButNotApplied_TellsTheNumberAndWhatToDoNext(HttpStatusCode status, string code, int exitCode)
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan());
        _cli.Server.On("POST", Propose, new { changeSet = new { number = 7, status = "pending" } }, HttpStatusCode.Created);
        _cli.Server.OnError("POST", $"{Propose}/7/apply", status, code, "資料在這之間變了。", ("x", "細項"));
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var human = await _cli.RunAsync("blueprint", "apply", "-f", path);
        var json = await _cli.RunAsync("blueprint", "apply", "-f", path, "--json");

        // Assert
        foreach (var result in new[] { human, json })
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.EqualTo(exitCode));
                Assert.That(result.Error, Does.Contain("已送出 #7，還沒套用").And.Contain("資料在這之間變了。"));
                Assert.That(result.Error, Does.Contain("kumi changeset apply 7 --project WEB").And.Contain("kumi changeset reject 7 --project WEB"));
                Assert.That(result.Error, Does.Contain("x：細項"));
            }
        }
    }

    [Test]
    public async Task Apply_ProposedButNoNumberComesBack_FailsWithoutCallingApply()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan());
        _cli.Server.On("POST", Propose, new { changeSet = new { status = "pending" } }, HttpStatusCode.Created);
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("沒有回變更集的編號").And.Contain("kumi changeset list --project WEB"));
            Assert.That(_cli.Server.Requests.Select(request => request.Path), Is.EqualTo(new[] { Preview, Propose }));
        }
    }

    // ---- --json 一律不問 ----

    [Test]
    public async Task Apply_JsonWithDataImpactAndNoYes_ExitsWithThreeWithoutAskingEvenOnATerminal()
    {
        // Arrange
        _cli.IsInteractive = true;
        _cli.StandardInput = "y\n";
        _cli.Server.On("POST", Preview, Plan(impacts: [new { kind = "state_move", affected = 2, blocksApply = false, workflow = "main", from = "a", to = "b" }]));
        var path = WriteFile("apply.yaml", "# project: WEB\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path, "--json");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.NeedsDecision));
            Assert.That(result.Error, Does.Not.Contain("要套用嗎"));
            Assert.That(_cli.Server.Requests, Has.Count.EqualTo(1), "沒有送出");
        }
    }

    [Test]
    public async Task Export_ToAFileWithJson_PrintsNothingForHumans()
    {
        // Arrange
        _cli.Server.On("GET", "/v1/projects/WEB", new { project = new { key = "WEB", blueprintVersion = 2 }, blueprint = new { a = 1 }, myPermissions = new[] { "blueprint.read" } });
        var path = _cli.PathOf("out.json");

        // Act
        var result = await _cli.RunAsync("blueprint", "export", "-p", "WEB", "-o", path, "--json");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Is.Empty);
        Assert.That(File.Exists(path), Is.True);
    }

    // ---- --summary 和伺服器讀得一樣 ----

    [Test]
    public async Task Summary_KeepsTheValuesTheServerWouldRead()
    {
        // Arrange
        _cli.Server.On("POST", Preview, Plan(canApply: false));
        _cli.Server.On("POST", Propose, new { changeSet = new { number = 1 } }, HttpStatusCode.Created);
        var path = WriteFile(
            "apply.yaml",
            "# project: WEB\nbaseVersion: 0x10\nsummary: 舊的\nblueprint:\n  big: 99999999999999999999\n  octal: 0o17\n  quoted: \"12\"\n  words: yes\n  nothing: ~\n  text: |\n    第一行\n    第二行\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path, "--summary", "新的");

        // Assert
        var body = JsonNode.Parse(_cli.Server.To("POST", Propose).Single().Body)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That((string?)body["summary"], Is.EqualTo("新的"), "已經有的 summary 被取代");
            Assert.That((long?)body["baseVersion"], Is.EqualTo(16));
            Assert.That((string?)body["blueprint"]!["big"], Is.EqualTo("99999999999999999999"), "放不進 long 的整數是字串，不是被轉成浮點數");
            Assert.That((long?)body["blueprint"]!["octal"], Is.EqualTo(15));
            Assert.That((string?)body["blueprint"]!["quoted"], Is.EqualTo("12"));
            Assert.That((string?)body["blueprint"]!["words"], Is.EqualTo("yes"));
            Assert.That(body["blueprint"]!["nothing"], Is.Null);
            Assert.That((string?)body["blueprint"]!["text"], Is.EqualTo("第一行\n第二行\n"));
        }
    }

    [TestCase("baseVersion: !!str 1\n", "標籤")]
    [TestCase("baseVersion: &a 1\nother: *a\n", "anchor")]
    [TestCase("? [a, b]\n: 1\n", "鍵是 mapping 或 sequence")]
    [TestCase("a: 1\na: 2\n", "重複")]
    [TestCase("a: 1\n---\nb: 2\n", "第二份文件")]
    public async Task Summary_YamlThatTheServerRejects_ExitsWithTwoAndSaysWhatIsWrong(string yaml, string expected)
    {
        // Arrange
        var path = WriteFile("apply.yaml", $"# project: WEB\n{yaml}");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path, "--summary", "說明");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("--summary").And.Contain(expected));
            Assert.That(_cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Summary_DocumentThatIsNotAnObject_IsAUsageError()
    {
        // Arrange
        var path = WriteFile("apply.yaml", "# project: WEB\n- 1\n- 2\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "apply", "-f", path, "--summary", "說明");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(_cli.Server.Requests, Is.Empty);
    }

    // ---- validate ----

    [Test]
    public async Task Validate_ComplexYamlKey_IsReportedAsAnIssueWithExitCodeTwo()
    {
        // Arrange
        var path = WriteFile("complex.yaml", "? [a, b]\n: 1\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "validate", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("鍵是 mapping 或 sequence"));
    }

    [Test]
    public async Task Validate_Success_SeparatesTheFileNameFromTheSentenceAndSaysWhatKindOfDocumentItIs()
    {
        // Arrange
        var path = Path.Combine(AppContext.BaseDirectory, "Examples", "split-acceptance.changeset.yaml");

        // Act
        var result = await _cli.RunAsync("blueprint", "validate", "-f", path);

        // Assert
        Assert.That(result.Out, Does.Contain($"{path} 沒有問題（這是一份變更集）。"));
    }

    [Test]
    public async Task Validate_KindIsDecidedByTheSameReaderAsTheServer()
    {
        // Arrange：用 !!str 標籤時伺服器的讀法會拒絕，所以不能被當成藍圖文件。
        var path = WriteFile("tagged.yaml", "apiVersion: !!str blueprint/v0\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "validate", "-f", path, "--json");

        // Assert
        var report = JsonNode.Parse(result.Out)!;
        Assert.That((string?)report["kind"], Is.EqualTo("changeset"));
        Assert.That((bool?)report["valid"], Is.False);
    }
}
