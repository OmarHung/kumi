using System.Text.Json;

namespace PmTool.Cli.Tests;

/// <summary>kumi blueprint export 與 validate：匯出的檔案、本機驗證。</summary>
[TestFixture]
public sealed class BlueprintExportAndValidateCommandTests
{
    private CliHarness _cli = null!;

    [SetUp]
    public void CreateHarness() => _cli = new CliHarness().LoggedInByEnvironment();

    [TearDown]
    public void DisposeHarness() => _cli.Dispose();

    private string WriteFile(string name, string text)
    {
        var path = _cli.PathOf(name);
        File.WriteAllText(path, text);
        return path;
    }

    [Test]
    public async Task Export_WritesTheVersionAndBlueprintAsYamlWithTheProjectComment()
    {
        // Arrange
        var blueprint = new { apiVersion = "blueprint/v0", kind = "ProjectBlueprint", metadata = new { key = "web", name = "官網" } };
        _cli.Server.On("GET", "/v1/projects/WEB", new
        {
            project = new { key = "WEB", blueprintVersion = 7 },
            blueprint,
            myPermissions = new[] { "blueprint.read", "item.read" },
        });
        var path = _cli.PathOf("web.yaml");

        // Act
        var result = await _cli.RunAsync("blueprint", "export", "--project", "WEB", "-o", path);

        // Assert
        var written = await File.ReadAllTextAsync(path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("第 7 版").And.Contain(path));
            Assert.That(written, Does.StartWith("# project: WEB\nbaseVersion: 7\nblueprint:\n"));
            Assert.That(written.Contains('\uFEFF'), Is.False, "不加 BOM");
        }
    }

    [Test]
    public async Task Export_WithoutAnOutputFile_PrintsTheDocumentToStandardOutput()
    {
        // Arrange
        _cli.Server.On("GET", "/v1/projects/WEB", new { project = new { key = "WEB", blueprintVersion = 2 }, blueprint = new { a = 1 }, myPermissions = new[] { "blueprint.read" } });

        // Act
        var yaml = await _cli.RunAsync("blueprint", "export", "--project", "WEB");
        var json = await _cli.RunAsync("blueprint", "export", "--project", "WEB", "--format", "json");
        var jsonFlag = await _cli.RunAsync("blueprint", "export", "--project", "WEB", "--json");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(yaml.Out, Does.StartWith("# project: WEB\n"));
            Assert.That(JsonDocument.Parse(json.Out).RootElement.GetProperty("baseVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(jsonFlag.Out, Is.EqualTo(json.Out));
        }
    }

    [Test]
    public async Task Export_OutputFileEndingInJson_WritesJson()
    {
        // Arrange
        _cli.Server.On("GET", "/v1/projects/WEB", new { project = new { key = "WEB", blueprintVersion = 2 }, blueprint = new { a = 1 }, myPermissions = new[] { "blueprint.read" } });
        var path = _cli.PathOf("web.json");

        // Act
        await _cli.RunAsync("blueprint", "export", "-p", "WEB", "-o", path);

        // Assert
        Assert.That(JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement.GetProperty("blueprint").GetProperty("a").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task Export_WithoutBlueprintReadPermission_RefusesBecauseTheBlueprintWouldBeIncomplete()
    {
        // Arrange
        _cli.Server.On("GET", "/v1/projects/WEB", new { project = new { key = "WEB", blueprintVersion = 2 }, blueprint = new { a = 1 }, myPermissions = new[] { "item.read" } });

        // Act
        var result = await _cli.RunAsync("blueprint", "export", "--project", "WEB");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Out, Is.Empty);
        Assert.That(result.Error, Does.Contain("不匯出"));
    }

    [Test]
    public async Task Export_UnknownFormat_IsAUsageError()
    {
        // Act
        var result = await _cli.RunAsync("blueprint", "export", "--project", "WEB", "--format", "xml");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(_cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task Export_OutputPathCannotBeWritten_ExitsWithOne()
    {
        // Arrange
        _cli.Server.On("GET", "/v1/projects/WEB", new { project = new { key = "WEB", blueprintVersion = 2 }, blueprint = new { a = 1 }, myPermissions = new[] { "blueprint.read" } });

        // Act
        var result = await _cli.RunAsync("blueprint", "export", "-p", "WEB", "-o", _cli.PathOf("missing-folder/web.yaml"));

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("寫不進"));
    }

    [Test]
    public async Task Validate_ValidExampleChangeSet_PassesWithoutTouchingTheNetwork()
    {
        // Arrange
        var path = Path.Combine(AppContext.BaseDirectory, "Examples", "split-acceptance.changeset.yaml");
        var cleanHarness = new CliHarness();

        // Act
        var result = await cleanHarness.RunAsync("blueprint", "validate", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("沒有問題").And.Contain("變更集"));
        Assert.That(cleanHarness.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task Validate_ABlueprintDocument_IsCheckedAsABlueprint()
    {
        // Arrange
        var path = Path.Combine(AppContext.BaseDirectory, "Examples", "website-redesign.blueprint.yaml");

        // Act
        var result = await _cli.RunAsync("blueprint", "validate", "-f", path);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("藍圖文件"));
    }

    [Test]
    public async Task Validate_InvalidDocument_ListsEveryIssueWithItsPathAndExitsWithTwo()
    {
        // Arrange
        var path = WriteFile("bad.yaml", "baseVersion: x\nblueprint: 1\n");

        // Act
        var result = await _cli.RunAsync("blueprint", "validate", "-f", path);
        var json = await _cli.RunAsync("blueprint", "validate", "-f", path, "--json");

        // Assert
        var report = JsonDocument.Parse(json.Out).RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("個問題").And.Contain("/baseVersion"));
            Assert.That(json.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(report.GetProperty("valid").GetBoolean(), Is.False);
            Assert.That(report.GetProperty("kind").GetString(), Is.EqualTo("changeset"));
            Assert.That(report.GetProperty("issues")[0].GetProperty("code").GetString(), Is.Not.Empty);
        }
    }

    [Test]
    public async Task Validate_StandardInput_ReadsFromTheSharedStream()
    {
        // Arrange
        _cli.StandardInput = "{ \"baseVersion\": ";

        // Act
        var result = await _cli.RunAsync("blueprint", "validate", "-f", "-");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("標準輸入"));
    }

    [Test]
    public async Task Validate_MissingFile_ExitsWithOne()
    {
        // Act
        var result = await _cli.RunAsync("blueprint", "validate", "-f", _cli.PathOf("nope.yaml"));

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("讀不到檔案"));
    }
}
