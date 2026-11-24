using static PmTool.Work.Tests.AttachmentSupport;

namespace PmTool.Work.Tests.Attachments;

/// <summary>檔名、類型、大小與數量的規則。</summary>
[TestFixture]
public sealed class AttachmentRulesTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCase("", TestName = "AddAsync_EmptyName_IsRejected")]
    [TestCase("   ", TestName = "AddAsync_BlankName_IsRejected")]
    [TestCase(".", TestName = "AddAsync_DotName_IsRejected")]
    [TestCase("..", TestName = "AddAsync_DoubleDotName_IsRejected")]
    [TestCase("../etc/passwd", TestName = "AddAsync_NameWithForwardSlashes_IsRejected")]
    [TestCase("資料夾\\檔案.txt", TestName = "AddAsync_NameWithBackslash_IsRejected")]
    [TestCase("a\0b.txt", TestName = "AddAsync_NameWithNul_IsRejected")]
    [TestCase("a\nb.txt", TestName = "AddAsync_NameWithLineBreak_IsRejected")]
    [TestCase("a\tb.txt", TestName = "AddAsync_NameWithTab_IsRejected")]
    [TestCase("a\u200Bb.txt", TestName = "AddAsync_NameWithZeroWidthSpace_IsRejected")]
    [TestCase("a\u202Etxt.exe", TestName = "AddAsync_NameWithRightToLeftOverride_IsRejected")]
    [TestCase("a\u2028b.txt", TestName = "AddAsync_NameWithLineSeparator_IsRejected")]
    [TestCase("LONE_SURROGATE", TestName = "AddAsync_NameWithLoneSurrogate_IsRejected")]
    public async Task AddAsync_BadFileName_IsRejectedWithoutReadingTheContentOrStoringAnything(string fileName)
    {
        // Arrange：落單的代理字元放不進測試的參數（會被換掉），在這裡才組出來。
        fileName = fileName == "LONE_SURROGATE" ? "a" + (char)0xD800 + "b.txt" : fileName;
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var filesBefore = FilesIn(_host).Length;
        await using var content = new UnreadableStream();

        // Act
        var added = await _host.Attachments.AddAsync(
            scenario.Admin, item.Key, new NewAttachment { FileName = fileName, Content = content }, CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNameInvalid));
            Assert.That(added.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "fileName" }));
            Assert.That(content.WasRead, Is.False);
            Assert.That(listed.Value, Is.Empty);
            Assert.That(FilesIn(_host), Has.Length.EqualTo(filesBefore));
        }
    }

    [Test]
    public async Task AddAsync_NameOf256Characters_IsRejectedAnd255IsAccepted()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var tooLong = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload(new string('長', 256), "x"), CancellationToken.None);
        var longest = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload(new string('長', 255), "x"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tooLong.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNameInvalid));
            Assert.That(longest.Value.FileName, Has.Length.EqualTo(255));
        }
    }

    [Test]
    public async Task AddAsync_NameWithSpacesAroundIt_IsStoredTrimmed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("  規格 (最終).pdf \t", "x"), CancellationToken.None);

        // Assert
        Assert.That(added.Value.FileName, Is.EqualTo("規格 (最終).pdf"));
    }

    [TestCase("名字.txt", TestName = "AddAsync_ChineseName_IsAccepted")]
    [TestCase("report v2 [final].tar.gz", TestName = "AddAsync_NameWithSpacesAndBrackets_IsAccepted")]
    [TestCase(".gitignore", TestName = "AddAsync_NameStartingWithADot_IsAccepted")]
    [TestCase("圖\U0001F600.png", TestName = "AddAsync_NameWithEmoji_IsAccepted")]
    [TestCase("...", TestName = "AddAsync_ThreeDots_IsAccepted")]
    public async Task AddAsync_OrdinaryFileName_IsAccepted(string fileName)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload(fileName, "x"), CancellationToken.None);

        // Assert
        Assert.That(added.Value.FileName, Is.EqualTo(fileName));
    }

    [TestCase("image/png", "image/png")]
    [TestCase("image/png; charset=x", "image/png")]
    [TestCase("IMAGE/PNG;charset=utf-8", "image/png")]
    [TestCase("  text/plain  ", "text/plain")]
    [TestCase("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [TestCase("image/svg+xml", "image/svg+xml")]
    [TestCase(null, "application/octet-stream")]
    [TestCase("", "application/octet-stream")]
    [TestCase("   ", "application/octet-stream")]
    [TestCase("png", "application/octet-stream")]
    [TestCase("image/", "application/octet-stream")]
    [TestCase("/png", "application/octet-stream")]
    [TestCase("image/png/extra", "application/octet-stream")]
    [TestCase("text/html<script>", "application/octet-stream")]
    [TestCase("text/plain\r\nX-Evil: 1", "application/octet-stream")]
    [TestCase("; charset=utf-8", "application/octet-stream")]
    public async Task AddAsync_ContentType_IsReducedToTypeSlashSubtype(string? reported, string expected)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var added = await _host.Attachments.AddAsync(
            scenario.Admin, item.Key, new NewAttachment { FileName = "a.bin", ContentType = reported, Content = new MemoryStream([1]) }, CancellationToken.None);

        // Assert
        Assert.That(added.Value.ContentType, Is.EqualTo(expected));
    }

    [Test]
    public async Task AddAsync_ContentTypeLongerThan200Characters_BecomesOctetStream()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var tooLong = "text/" + new string('a', 196);

        // Act
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", [1], tooLong), CancellationToken.None);

        // Assert
        Assert.That(added.Value.ContentType, Is.EqualTo("application/octet-stream"));
    }

    [Test]
    public async Task AddAsync_FileOfExactlyTheLimit_IsAccepted()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("最大.zip", AttachmentRules.MaxBytes), CancellationToken.None);

        // Assert
        Assert.That(added.Value.SizeBytes, Is.EqualTo(AttachmentRules.MaxBytes));
    }

    [Test]
    public async Task AddAsync_FileOneByteOverTheLimit_AbortsAndLeavesNothingInTheStorage()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var filesBefore = FilesIn(_host).ToHashSet();

        // Act
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("太大.zip", AttachmentRules.MaxBytes + 1), CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentTooLarge));
            Assert.That(added.Failure?.Kind, Is.EqualTo(PmTool.Abstractions.FailureKind.Invalid));
            Assert.That(listed.Value, Is.Empty);
            Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
            Assert.That(FilesIn(_host).ToHashSet(), Is.EquivalentTo(filesBefore));
        }
    }

    [Test]
    public async Task AddAsync_The101stAttachment_IsRejectedAndLeavesNothingInTheStorage()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        await FillAsync(scenario, item.Key, AttachmentRules.MaxPerItem);
        var filesBefore = FilesIn(_host).Length;

        // Act
        await using var content = new UnreadableStream();
        var added = await _host.Attachments.AddAsync(
            scenario.Admin, item.Key, new NewAttachment { FileName = "第101個.txt", Content = content }, CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentLimitReached));
            Assert.That(content.WasRead, Is.False, "數量已滿時不讀內容");
            Assert.That(listed.Value, Has.Count.EqualTo(AttachmentRules.MaxPerItem));
            Assert.That(FilesIn(_host), Has.Length.EqualTo(filesBefore));
        }
    }

    [Test]
    public async Task AddAsync_AfterDeletingOne_AllowsTheNextAttachment()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        await FillAsync(scenario, item.Key, AttachmentRules.MaxPerItem);
        var first = (await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None)).Value[0];
        await _host.Attachments.DeleteAsync(scenario.Admin, first.Id, CancellationToken.None);

        // Act
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("補上.txt", "x"), CancellationToken.None);

        // Assert
        Assert.That(added.Succeeded, Is.True);
    }

    [Test]
    public async Task AddAsync_UploadsAtTheSameTimeNeverGoPastTheLimitAndLoserFilesAreRemoved()
    {
        // Arrange：已經 98 個，同時再傳 6 個，只有 2 個進得去。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        await FillAsync(scenario, item.Key, AttachmentRules.MaxPerItem - 2);
        var filesBefore = FilesIn(_host).Length;

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            Task.Run(() => _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload($"同時{i}.txt", $"內容{i}"), CancellationToken.None))));
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(2));
            Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code).Distinct(), Is.EqualTo(new[] { WorkErrorCodes.AttachmentLimitReached }));
            Assert.That(listed.Value, Has.Count.EqualTo(AttachmentRules.MaxPerItem));
            Assert.That(FilesIn(_host), Has.Length.EqualTo(filesBefore + 2), "沒進去的上傳，檔案都清掉了");
        }
    }

    private async Task FillAsync(Scenario scenario, string itemKey, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var added = await _host.Attachments.AddAsync(scenario.Admin, itemKey, Upload($"f{i}.txt", "x"), CancellationToken.None);
            Assert.That(added.Succeeded, Is.True, () => added.Failure!.Message);
        }
    }
}
