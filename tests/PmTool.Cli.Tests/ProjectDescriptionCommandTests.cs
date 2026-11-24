using System.Globalization;
using System.Net;

namespace PmTool.Cli.Tests;

/// <summary>專案說明：kumi project info 與 kumi project describe。</summary>
[TestFixture]
public sealed class ProjectDescriptionCommandTests
{
    private const string UpdatedAt = "2026-11-20T08:05:00+00:00";
    private const string Path = "/v1/projects/WEB/description";

    private static CliHarness LoggedIn() => new CliHarness().LoggedInByEnvironment();

    private static object Written(string? text = "## 網址\n- 正式：https://www.example.com") =>
        new { text, updatedAt = UpdatedAt, updatedBy = new { personId = Guid.Parse("0190a000-0000-7000-8000-0000000000a1"), displayName = "王小明" } };

    private static string Local(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    [Test]
    public async Task Info_PrintsTheMarkdownAsIsThenWhoUpdatedItWhen()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Path, Written("## 網址\n- 正式：https://www.example.com\n"));

        // Act
        var result = await cli.RunAsync("project", "info", "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(
                result.Out.ReplaceLineEndings("\n"),
                Is.EqualTo($"## 網址\n- 正式：https://www.example.com\n\n最後由 王小明 於 {Local(UpdatedAt)} 更新\n"));
        }
    }

    [Test]
    public async Task Info_NeverWritten_SaysThereIsNoDescriptionWithExitCode0()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Path, new { text = (string?)null, updatedAt = (string?)null, updatedBy = (object?)null });

        // Act
        var result = await cli.RunAsync("project", "info", "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out.Trim(), Is.EqualTo("這個專案還沒有說明。"));
        }
    }

    [Test]
    public async Task Info_ClearedDescription_SaysThereIsNoDescription()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Path, Written(null));

        // Act
        var result = await cli.RunAsync("project", "info", "WEB");

        // Assert
        Assert.That(result.Out.Trim(), Is.EqualTo("這個專案還沒有說明。"));
    }

    [Test]
    public async Task Info_Json_PrintsTheServersDescriptionAsIs()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Path, Written("內容"));

        // Act
        var result = await cli.RunAsync("project", "info", "WEB", "--json");

        // Assert
        var json = System.Text.Json.Nodes.JsonNode.Parse(result.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json["text"]!.GetValue<string>(), Is.EqualTo("內容"));
            Assert.That(json["updatedBy"]!["displayName"]!.GetValue<string>(), Is.EqualTo("王小明"));
            Assert.That(result.Out, Does.Not.Contain("最後由"));
        }
    }

    [Test]
    public async Task Info_ProjectNotFound_ExitsWith1AndShowsTheServersMessage()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("GET", Path, HttpStatusCode.NotFound, "project.not_found", "找不到專案 WEB。");

        // Act
        var result = await cli.RunAsync("project", "info", "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("找不到專案"));
        }
    }

    [Test]
    public async Task Describe_FromAFile_PutsTheFileTextUnchanged()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("PUT", Path, Written("## 主機\n  - 縮排\n"));
        var file = cli.PathOf("info.md");
        await File.WriteAllTextAsync(file, "## 主機\n  - 縮排\n");

        // Act
        var result = await cli.RunAsync("project", "describe", "WEB", "--file", file);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(cli.Server.To("PUT", Path).Single().Json!["text"]!.GetValue<string>(), Is.EqualTo("## 主機\n  - 縮排\n"));
            Assert.That(result.Out.Trim(), Is.EqualTo("已更新 WEB 的專案說明。"));
        }
    }

    [Test]
    public async Task Describe_FromStandardInput_PutsTheInputText()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.StandardInput = "- 正式：https://www.example.com\n";
        cli.Server.On("PUT", Path, Written("x"));

        // Act
        var result = await cli.RunAsync("project", "describe", "WEB", "--file", "-");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(cli.Server.To("PUT", Path).Single().Json!["text"]!.GetValue<string>(), Is.EqualTo("- 正式：https://www.example.com\n"));
        }
    }

    [Test]
    public async Task Describe_Clear_PutsNull()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("PUT", Path, Written(null));

        // Act
        var result = await cli.RunAsync("project", "describe", "WEB", "--clear");

        // Assert
        var body = cli.Server.To("PUT", Path).Single().Json!.AsObject();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(body.ContainsKey("text") && body["text"] is null, Is.True);
            Assert.That(result.Out.Trim(), Is.EqualTo("已清除 WEB 的專案說明。"));
        }
    }

    [Test]
    public async Task Describe_Json_PrintsTheServersDescription()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("PUT", Path, Written("內容"));

        // Act
        var result = await cli.RunAsync("project", "describe", "WEB", "--clear", "--json");

        // Assert
        Assert.That(System.Text.Json.Nodes.JsonNode.Parse(result.Out)!["text"]!.GetValue<string>(), Is.EqualTo("內容"));
    }

    [Test]
    public async Task Describe_NeitherFileNorClear_ExitsWithUsageErrorAndSendsNothing()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("project", "describe", "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("--file").And.Contain("--clear"));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Describe_BothFileAndClear_ExitsWithUsageErrorAndSendsNothing()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("project", "describe", "WEB", "--file", "x.md", "--clear");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Describe_MissingFile_ExitsWithErrorAndSendsNothing()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("project", "describe", "WEB", "--file", cli.PathOf("nope.md"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("nope.md"));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Describe_BlankInput_RefusesInsteadOfSilentlyClearing()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.StandardInput = "  \n";

        // Act
        var result = await cli.RunAsync("project", "describe", "WEB", "--file", "-");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("--clear"));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Describe_ServerRejectsThePrivateKey_ExitsWith2AndShowsThePath()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.StandardInput = "key";
        cli.Server.OnError("PUT", Path, HttpStatusCode.UnprocessableEntity, "project.description_secret", "說明有問題。", ("text", "說明裡有私鑰。"));

        // Act
        var result = await cli.RunAsync("project", "describe", "WEB", "--file", "-");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("說明有問題").And.Contain("text").And.Contain("說明裡有私鑰"));
    }

    [Test]
    public async Task Formats_ListsIdNameAndTheDefaultAndBuiltInMarks()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/description-templates", new object[]
        {
            new { id = "0190a000-0000-7000-8000-0000000000b1", name = "維運", body = "## 主機", isBuiltIn = false, isDefault = true },
            new { id = "0190a000-0000-7000-8000-0000000000b2", name = "案子", body = "x", isBuiltIn = false, isDefault = false },
            new { id = "web_project", name = "網站案子", body = "## 基本資料", isBuiltIn = true, isDefault = false },
        });

        // Act
        var result = await cli.RunAsync("project", "formats");

        // Assert
        var lines = result.Out.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(lines[0], Does.Contain("代號").And.Contain("名稱").And.Contain("標記"));
            Assert.That(lines[1], Does.Contain("0190a000-0000-7000-8000-0000000000b1").And.Contain("維運").And.Contain("預設").And.Not.Contain("內建"));
            Assert.That(lines[2], Does.Not.Contain("預設").And.Not.Contain("內建"));
            Assert.That(lines[3], Does.Contain("web_project").And.Contain("網站案子").And.Contain("內建").And.Not.Contain("預設"));
            Assert.That(result.Out, Does.Not.Contain("## 基本資料"));
        }
    }

    [Test]
    public async Task Formats_Json_PrintsTheServersTemplatesWithTheirBodies()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/description-templates", new object[]
        {
            new { id = "web_project", name = "網站案子", body = "## 基本資料", isBuiltIn = true, isDefault = true },
        });

        // Act
        var result = await cli.RunAsync("project", "formats", "--json");

        // Assert
        var json = System.Text.Json.Nodes.JsonNode.Parse(result.Out)!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(json.Single()!["body"]!.GetValue<string>(), Is.EqualTo("## 基本資料"));
            Assert.That(json.Single()!["isDefault"]!.GetValue<bool>(), Is.True);
        }
    }
}
