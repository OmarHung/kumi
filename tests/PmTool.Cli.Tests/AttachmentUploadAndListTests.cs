using System.Net;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Tests;

/// <summary>kumi item attach、kumi item attachments：上傳前的檢查、送出的內容、列表與錯誤。</summary>
[TestFixture]
public sealed class AttachmentUploadAndListTests
{
    private const string Created = "/v1/items/WEB-12/attachments";
    private const long MaxBytes = 25L * 1024 * 1024;

    private static CliHarness LoggedIn() => new CliHarness().LoggedInByEnvironment();

    private static object Attachment(string fileName, long size, string id = "11111111-1111-1111-1111-111111111111") => new
    {
        id, itemKey = "WEB-12", fileName, contentType = "text/plain", sizeBytes = size, sha256 = new string('a', 64),
        uploadedBy = new { personId = "p1", displayName = "王小明" }, uploadedAt = "2026-11-19T01:02:00Z",
    };

    private static string WriteFile(CliHarness cli, string name, string content)
    {
        var path = cli.PathOf(name);
        File.WriteAllText(path, content);
        return path;
    }

    // ---- 上傳 ----

    [Test]
    public async Task Attach_OneFile_SendsMultipartWithFieldFileAndTheNameAndContent()
    {
        // Arrange
        using var cli = LoggedIn();
        var path = WriteFile(cli, "report.txt", "hello example");
        cli.Server.On("POST", Created, Attachment("report.txt", 13), HttpStatusCode.Created);

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12", path);

        // Assert
        var request = cli.Server.To("POST", Created).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(request.ContentType, Is.EqualTo("multipart/form-data"));
            Assert.That(request.Body, Does.Contain("name=file").And.Contain("filename=report.txt").And.Contain("hello example"));
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {CliHarness.Token}"));
            Assert.That(result.Out, Does.Contain("已附加 report.txt（13 B）"));
        }
    }

    [Test]
    public async Task Attach_ChineseFileName_IsEncodedSoTheServerCanReadItBack()
    {
        // Arrange
        using var cli = LoggedIn();
        var path = WriteFile(cli, "規格.txt", "x");
        cli.Server.On("POST", Created, Attachment("規格.txt", 1), HttpStatusCode.Created);

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12", path);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(cli.Server.To("POST", Created).Single().Body, Does.Contain("filename*=utf-8''%E8%A6%8F%E6%A0%BC.txt"));
        }
    }

    [Test]
    public async Task Attach_RelativePath_IsResolvedAgainstTheWorkingDirectory()
    {
        // Arrange
        using var cli = LoggedIn();
        File.WriteAllText(Path.Combine(cli.WorkingDirectory, "note.txt"), "x");
        cli.Server.On("POST", Created, Attachment("note.txt", 1), HttpStatusCode.Created);

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12", "note.txt");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
    }

    [Test]
    public async Task Attach_PngFile_SendsAnImageContentType()
    {
        // Arrange
        using var cli = LoggedIn();
        var path = WriteFile(cli, "shot.PNG", "not really a png");
        cli.Server.On("POST", Created, Attachment("shot.PNG", 16), HttpStatusCode.Created);

        // Act
        await cli.RunAsync("item", "attach", "WEB-12", path);

        // Assert
        Assert.That(cli.Server.To("POST", Created).Single().Body, Does.Contain("Content-Type: image/png"));
    }

    [Test]
    public async Task Attach_FileTooLarge_IsNotSentAndExitsWithTwo()
    {
        // Arrange
        using var cli = LoggedIn();
        var path = cli.PathOf("big.bin");
        using (var stream = File.Create(path))
        {
            stream.SetLength(MaxBytes + 1);
        }

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12", path);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("big.bin").And.Contain("25 MiB"));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Attach_FileExactlyAtTheLimit_IsSent()
    {
        // Arrange
        using var cli = LoggedIn();
        var path = cli.PathOf("edge.bin");
        using (var stream = File.Create(path))
        {
            stream.SetLength(MaxBytes);
        }

        cli.Server.On("POST", Created, Attachment("edge.bin", MaxBytes), HttpStatusCode.Created);

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12", path);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("25.0 MB"));
        }
    }

    [Test]
    public async Task Attach_MissingFileOrDirectory_IsNotSentAndExitsWithTwo()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var missing = await cli.RunAsync("item", "attach", "WEB-12", cli.PathOf("nope.txt"));
        var directory = await cli.RunAsync("item", "attach", "WEB-12", cli.WorkingDirectory);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(missing.Error, Does.Contain("找不到"));
            Assert.That(directory.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(directory.Error, Does.Contain("資料夾"));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    [Platform(Exclude = "Win")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Attach_UnreadableFile_IsNotSentAndExitsWithTwo()
    {
        // Arrange
        using var cli = LoggedIn();
        var path = WriteFile(cli, "secret.txt", "x");
        File.SetUnixFileMode(path, UnixFileMode.None);

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12", path);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("讀不了"));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Attach_SeveralFilesWithOneRejected_UploadsTheOthersAndReportsTheFailure()
    {
        // Arrange
        using var cli = LoggedIn();
        // 內容用十六進位以外的字：multipart 的分隔線是隨機的十六進位，「aaa」「bbb」偶爾會出現在分隔線裡，認錯檔案
        var first = WriteFile(cli, "a.txt", "file-one");
        var second = WriteFile(cli, "b.txt", "file-two");
        var third = WriteFile(cli, "c.txt", "file-three");
        cli.Server.On("POST", Created, request => request.Body.Contains("file-two")
            ? new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("""{"success":false,"data":null,"error":{"code":"attachment.limit_reached","message":"這個項目的附件已經滿了。","details":[]}}"""),
            }
            : Fake.Created(request.Body.Contains("file-one") ? "a.txt" : "c.txt"));

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12", first, second, third);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(cli.Server.To("POST", Created), Has.Count.EqualTo(3));
            Assert.That(result.Out, Does.Contain("已附加 a.txt").And.Contain("已附加 c.txt").And.Not.Contain("b.txt"));
            Assert.That(result.Error, Does.Contain(second).And.Contain("附件已經滿了"));
        }
    }

    [Test]
    public async Task Attach_FirstFileInvalidSecondFine_StillUploadsTheSecondAndExitsWithTwo()
    {
        // Arrange
        using var cli = LoggedIn();
        var good = WriteFile(cli, "good.txt", "ok");
        cli.Server.On("POST", Created, Attachment("good.txt", 2), HttpStatusCode.Created);

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12", cli.PathOf("missing.txt"), good);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(cli.Server.To("POST", Created), Has.Count.EqualTo(1));
            Assert.That(result.Out, Does.Contain("已附加 good.txt"));
        }
    }

    [Test]
    public async Task Attach_Json_PrintsTheCreatedAttachmentsAsAnArray()
    {
        // Arrange
        using var cli = LoggedIn();
        var path = WriteFile(cli, "report.txt", "hi");
        cli.Server.On("POST", Created, Attachment("report.txt", 2), HttpStatusCode.Created);

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12", path, "--json");

        // Assert
        var json = JsonNode.Parse(result.Out)!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Has.Count.EqualTo(1));
            Assert.That((string?)json[0]!["fileName"], Is.EqualTo("report.txt"));
            Assert.That(result.Out, Does.Not.Contain("已附加"));
        }
    }

    [TestCase(HttpStatusCode.RequestEntityTooLarge, "attachment.too_large", "附件太大了。", ExitCodes.Invalid)]
    [TestCase(HttpStatusCode.Forbidden, "forbidden", "你沒有權限", ExitCodes.Error)]
    [TestCase(HttpStatusCode.NotFound, "item.not_found", "找不到這個項目。", ExitCodes.Error)]
    [TestCase(HttpStatusCode.UnprocessableEntity, "attachment.name_invalid", "檔名不合規則。", ExitCodes.Invalid)]
    public async Task Attach_ServerRejects_ShowsTheMessageAndMapsTheExitCode(HttpStatusCode status, string code, string message, int exitCode)
    {
        // Arrange
        using var cli = LoggedIn();
        var path = WriteFile(cli, "report.txt", "hi");
        cli.Server.OnError("POST", Created, status, code, message);

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12", path);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode));
            Assert.That(result.Error, Does.Contain(message));
            Assert.That(result.Out, Is.Empty);
        }
    }

    [Test]
    public async Task Attach_WithoutAFile_IsAUsageError()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "attach", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("kumi item attach"));
        }
    }

    // ---- 列出 ----

    [Test]
    public async Task Attachments_PrintsATableWithHumanSizesAndTheUploadersName()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Created, new[]
        {
            Attachment("規格.pdf", 1_258_291, "11111111-1111-1111-1111-111111111111"),
            Attachment("tiny.txt", 512, "22222222-2222-2222-2222-222222222222"),
        });

        // Act
        var result = await cli.RunAsync("item", "attachments", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("編號").And.Contain("檔名").And.Contain("大小").And.Contain("上傳的人").And.Contain("時間"));
            Assert.That(result.Out, Does.Contain("11111111-1111-1111-1111-111111111111").And.Contain("規格.pdf"));
            Assert.That(result.Out, Does.Contain("1.2 MB").And.Contain("512 B").And.Contain("王小明"));
            Assert.That(result.Out, Does.Not.Contain("p1"));
        }
    }

    [Test]
    public async Task Attachments_Json_PrintsTheServersData()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Created, new[] { Attachment("a.txt", 3) });

        // Act
        var result = await cli.RunAsync("item", "attachments", "WEB-12", "--json");

        // Assert
        var json = JsonNode.Parse(result.Out)!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((string?)json[0]!["sha256"], Is.EqualTo(new string('a', 64)));
            Assert.That((long?)json[0]!["sizeBytes"], Is.EqualTo(3));
        }
    }

    [Test]
    public async Task Attachments_None_SaysSo()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Created, Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("item", "attachments", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(result.Out, Does.Contain("WEB-12 沒有附件"));
        }
    }

    [Test]
    public async Task Attachments_ItemNotFound_ExitsWithOne()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("GET", Created, HttpStatusCode.NotFound, "item.not_found", "找不到這個項目。");

        // Act
        var result = await cli.RunAsync("item", "attachments", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("找不到這個項目"));
        }
    }

    private static class Fake
    {
        public static HttpResponseMessage Created(string fileName) => FakeServer.Envelope(HttpStatusCode.Created, Attachment(fileName, 3));
    }
}
