using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Tests;

/// <summary>kumi attachment download：存到哪裡、不覆蓋、sha256 核對，以及不安全的檔名。</summary>
[TestFixture]
public sealed class AttachmentDownloadTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private const string ContentPath = $"/v1/attachments/{Id}/content";
    private const string Body = "downloaded example content";

    private static CliHarness LoggedIn() => new CliHarness().LoggedInByEnvironment();

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static HttpResponseMessage File200(string content, string? disposition, string? sha = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        if (sha is not null)
        {
            response.Headers.TryAddWithoutValidation("X-Attachment-Sha256", sha);
        }

        if (disposition is not null)
        {
            response.Content.Headers.TryAddWithoutValidation("Content-Disposition", disposition);
        }

        return response;
    }

    private static CliHarness WithFile(string disposition, string content = Body)
    {
        var cli = LoggedIn();
        cli.Server.On("GET", ContentPath, _ => File200(content, disposition));
        return cli;
    }

    private static object Listed(string fileName, string sha) => new[]
    {
        new { id = Id, itemKey = "WEB-12", fileName, sizeBytes = 26, sha256 = sha },
    };

    // ---- 檔名與路徑 ----

    [Test]
    public async Task Download_ByDefault_SavesUnderTheOriginalNameInTheWorkingDirectory()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        var saved = Path.Combine(cli.WorkingDirectory, "report.txt");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.ReadAllText(saved), Is.EqualTo(Body));
            Assert.That(result.Out, Does.Contain(saved));
            Assert.That(Directory.GetFiles(cli.WorkingDirectory), Has.Length.EqualTo(1), "不留暫存檔");
            Assert.That(cli.Server.Requests.Single().Authorization, Is.EqualTo($"Bearer {CliHarness.Token}"));
        }
    }

    [Test]
    public async Task Download_Utf8EncodedName_KeepsTheChineseName()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"fallback.pdf\"; filename*=UTF-8''%E8%A6%8F%E6%A0%BC.pdf");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.Exists(Path.Combine(cli.WorkingDirectory, "規格.pdf")), Is.True);
        }
    }

    [Test]
    public async Task Download_OutputIsAFilePath_SavesThereWithoutRenaming()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");
        var target = cli.PathOf("mine.dat");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "-o", target);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.ReadAllText(target), Is.EqualTo(Body));
        }
    }

    [Test]
    public async Task Download_OutputIsADirectory_PutsTheOriginalNameInside()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");
        var directory = Directory.CreateDirectory(cli.PathOf("out")).FullName;

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "--output", directory);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.ReadAllText(Path.Combine(directory, "report.txt")), Is.EqualTo(Body));
        }
    }

    [Test]
    public async Task Download_OutputInAMissingFolder_ExitsWithTwoAndWritesNothing()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "-o", cli.PathOf("missing/x.txt"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("資料夾不存在"));
        }
    }

    [Test]
    public async Task Download_NoNameFromTheServer_FallsBackToTheAttachmentId()
    {
        // Arrange
        using var cli = WithFile(disposition: null!);

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.Exists(Path.Combine(cli.WorkingDirectory, $"attachment-{Id}")), Is.True);
        }
    }

    // ---- 惡意的檔名 ----

    [TestCase("attachment; filename=\"../x\"", "x")]
    [TestCase("attachment; filename=\"/etc/x\"", "x")]
    [TestCase("attachment; filename=\"..\\\\..\\\\x\"", "x")]
    [TestCase("attachment; filename*=UTF-8''..%2F..%2Fx", "x")]
    [TestCase("attachment; filename=\"safe.txt\"; filename*=UTF-8''..%2Fx", "x")]
    [TestCase("attachment; filename*=UTF-8''a%1B%5B31mb%00.txt", "a[31mb.txt")]
    [TestCase("attachment; filename*=UTF-8''%E2%80%AEtxt.exe", "txt.exe")]
    public async Task Download_HostileName_StaysInsideTheWorkingDirectory(string disposition, string expectedName)
    {
        // Arrange
        using var cli = WithFile(disposition);

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        var files = Directory.GetFiles(cli.WorkingDirectory);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(files.Select(Path.GetFileName), Is.EqualTo(new[] { expectedName }));
            Assert.That(Directory.GetDirectories(cli.WorkingDirectory), Is.Empty);
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(cli.WorkingDirectory)!, "x")), Is.False);
        }
    }

    [TestCase("attachment; filename=\"..\"")]
    [TestCase("attachment; filename=\".\"")]
    [TestCase("attachment; filename=\"a/..\"")]
    public async Task Download_NameThatIsOnlyDots_UsesTheFallbackName(string disposition)
    {
        // Arrange
        using var cli = WithFile(disposition);

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Directory.GetFiles(cli.WorkingDirectory).Select(Path.GetFileName), Is.EqualTo(new[] { $"attachment-{Id}" }));
        }
    }

    // ---- 已經存在 ----

    [Test]
    public async Task Download_TargetExists_ExitsWithTwoAndKeepsTheOldFile()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");
        var saved = Path.Combine(cli.WorkingDirectory, "report.txt");
        File.WriteAllText(saved, "old");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("--force"));
            Assert.That(File.ReadAllText(saved), Is.EqualTo("old"));
            Assert.That(Directory.GetFiles(cli.WorkingDirectory), Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task Download_TargetExistsWithForce_Overwrites()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");
        var saved = Path.Combine(cli.WorkingDirectory, "report.txt");
        File.WriteAllText(saved, "old");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "--force");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.ReadAllText(saved), Is.EqualTo(Body));
            Assert.That(Directory.GetFiles(cli.WorkingDirectory), Has.Length.EqualTo(1));
        }
    }

    // ---- sha256 ----

    [Test]
    public async Task Download_WithItem_VerifiesTheSha256AndUsesTheListedName()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"application-octet-stream-name\"");
        cli.Server.On("GET", "/v1/items/WEB-12/attachments", Listed("listed.txt", Sha(Body)));

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "--item", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.ReadAllText(Path.Combine(cli.WorkingDirectory, "listed.txt")), Is.EqualTo(Body));
            Assert.That(result.Error, Does.Not.Contain("沒有核對"));
        }
    }

    [Test]
    public async Task Download_Sha256Mismatch_LeavesNoFileAndExitsWithOne()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");
        cli.Server.On("GET", "/v1/items/WEB-12/attachments", Listed("report.txt", Sha("something else")));

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "--item", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("sha256"));
            Assert.That(Directory.GetFileSystemEntries(cli.WorkingDirectory), Is.Empty);
        }
    }

    [Test]
    public async Task Download_Sha256MismatchWithForce_KeepsTheExistingFile()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");
        cli.Server.On("GET", "/v1/items/WEB-12/attachments", Listed("report.txt", Sha("something else")));
        var saved = Path.Combine(cli.WorkingDirectory, "report.txt");
        File.WriteAllText(saved, "old");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "--item", "WEB-12", "--force");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(File.ReadAllText(saved), Is.EqualTo("old"));
            Assert.That(Directory.GetFiles(cli.WorkingDirectory), Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task Download_OldServerWithoutAnyHash_WarnsThatOnlyTheSizeWasChecked()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Does.Contain("沒有提供 sha256"));
        }
    }

    [Test]
    public async Task Download_HeaderHashMatches_SavesWithoutAWarning()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", ContentPath, _ => File200(Body, "attachment; filename=\"report.txt\"", Sha(Body)));

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Is.Empty);
            Assert.That(File.ReadAllText(Path.Combine(cli.WorkingDirectory, "report.txt")), Is.EqualTo(Body));
        }
    }

    [Test]
    public async Task Download_HeaderHashDiffers_LeavesNoFileAndExitsWithOne()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", ContentPath, _ => File200(Body, "attachment; filename=\"report.txt\"", Sha("other")));

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("sha256"));
            Assert.That(Directory.GetFileSystemEntries(cli.WorkingDirectory), Is.Empty);
        }
    }

    [Test]
    public async Task Download_ListAndHeaderDisagree_TheListWinsAndTheDifferenceIsExplained()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", ContentPath, _ => File200(Body, "attachment; filename=\"report.txt\"", Sha("other")));
        cli.Server.On("GET", "/v1/items/WEB-12/attachments", Listed("report.txt", Sha(Body)));

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "--item", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Does.Contain("不一樣"));
        }
    }

    [Test]
    public async Task Download_MalformedHeaderHash_IsIgnored()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", ContentPath, _ => File200(Body, "attachment; filename=\"report.txt\"", "not-a-hash"));

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Does.Contain("沒有提供 sha256"));
        }
    }

    [Test]
    public async Task Download_FilenameStarWinsOverAsciiFilename()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"_.pdf\"; filename*=UTF-8''%E4%B8%AD%E6%96%87%E8%AA%AA%E6%98%8E.pdf");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        Assert.That(File.Exists(Path.Combine(cli.WorkingDirectory, "中文說明.pdf")), Is.True, result.Error);
    }

    [Test]
    public async Task Download_ItemDoesNotHaveThatAttachment_ExitsWithOneBeforeDownloading()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");
        cli.Server.On("GET", "/v1/items/WEB-12/attachments", Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "--item", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(cli.Server.To("GET", ContentPath), Is.Empty);
        }
    }

    [Test]
    public async Task Download_Json_PrintsThePathAndHash()
    {
        // Arrange
        using var cli = WithFile("attachment; filename=\"report.txt\"");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "--json");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That((string?)json["path"], Is.EqualTo(Path.Combine(cli.WorkingDirectory, "report.txt")));
            Assert.That((string?)json["sha256"], Is.EqualTo(Sha(Body)));
        }
    }

    // ---- 錯誤 ----

    [TestCase(HttpStatusCode.NotFound, "attachment.not_found", "找不到這個附件。", ExitCodes.Error)]
    [TestCase(HttpStatusCode.Forbidden, "forbidden", "權杖的範圍不夠。", ExitCodes.Error)]
    [TestCase(HttpStatusCode.RequestEntityTooLarge, "attachment.too_large", "太大了。", ExitCodes.Invalid)]
    public async Task Download_ServerRejects_ShowsTheMessageAndLeavesNoFile(HttpStatusCode status, string code, string message, int exitCode)
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("GET", ContentPath, status, code, message);

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode));
            Assert.That(result.Error, Does.Contain(message));
            Assert.That(Directory.GetFileSystemEntries(cli.WorkingDirectory), Is.Empty);
        }
    }

    [Test]
    public async Task Download_ErrorPageWithoutAnEnvelope_ReportsTheStatus()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", ContentPath, _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") });

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("HTTP 502"));
        }
    }

    [Test]
    public async Task Download_IdThatIsNotAGuid_IsAUsageErrorAndSendsNothing()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("attachment", "download", "../../x");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Download_ConnectionDropsMidBody_ReportsANetworkErrorAndLeavesNoFile()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", ContentPath, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BrokenStream("partial")),
        });

        // Act
        var result = await cli.RunAsync("attachment", "download", Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("連不上伺服器").And.Not.Contain("寫不進").And.Not.Contain("已經存在"));
            Assert.That(Directory.GetFileSystemEntries(cli.WorkingDirectory), Is.Empty);
        }
    }

    [Test]
    public async Task Download_ConnectionDropsWithForce_KeepsTheExistingFileAndReportsTheNetworkError()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", ContentPath, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BrokenStream("partial")),
        });
        File.WriteAllText(Path.Combine(cli.WorkingDirectory, $"attachment-{Id}"), "old");

        // Act
        var result = await cli.RunAsync("attachment", "download", Id, "--force");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Does.Contain("連不上伺服器"));
            Assert.That(File.ReadAllText(Path.Combine(cli.WorkingDirectory, $"attachment-{Id}")), Is.EqualTo("old"));
        }
    }

    /// <summary>讀了一點資料之後丟 IOException，像連線中途被切斷。</summary>
    private sealed class BrokenStream(string firstChunk) : Stream
    {
        private bool _delivered;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_delivered)
            {
                throw new IOException("The response ended prematurely.");
            }

            _delivered = true;
            var bytes = Encoding.UTF8.GetBytes(firstChunk);
            bytes.CopyTo(buffer);
            return ValueTask.FromResult(bytes.Length);
        }
    }

    // ---- 說明 ----

    [Test]
    public async Task Help_ListsTheAttachmentCommands()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var overview = await cli.RunAsync("--help");
        var download = await cli.RunAsync("attachment", "download", "--help");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overview.Out, Does.Contain("item attach ").And.Contain("item attachments").And.Contain("attachment download"));
            Assert.That(download.Out, Does.Contain("用法：kumi attachment download").And.Contain("--force").And.Contain("--output"));
        }
    }
}
