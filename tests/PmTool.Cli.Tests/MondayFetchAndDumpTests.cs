using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Import.Monday;

namespace PmTool.Cli.Tests;

/// <summary>fetch 寫 dump、MondayDumpReader 讀回來：結構、分頁、留言接頁、檔案下載與沿用、不留下半份。</summary>
[TestFixture]
public sealed class MondayFetchAndDumpTests
{
    private string _root = null!;
    private FakeServer _server = null!;
    private FakeMondayApi _monday = null!;
    private StringWriter _progress = null!;

    private string Dump => Path.Combine(_root, "dump");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pm-monday-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _server = new FakeServer();
        _monday = new FakeMondayApi();
        _progress = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        _server.Dispose();
        _progress.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private Task<MondayFetchSummary> FetchAsync(bool files = false, MondayMapping? mapping = null, string? output = null)
    {
        _monday.Attach(_server);
        var http = new HttpClient(_server);
        var client = new MondayGraphQlClient(http, FakeMondayApi.Url, FakeMondayApi.Token, (_, _) => Task.CompletedTask);
        return new MondayFetcher(client, http, _progress).RunAsync(
            new MondayFetchOptions([FakeMondayApi.BoardId], output ?? Dump, mapping, files), CancellationToken.None);
    }

    private static MondayItem ItemById(MondayDump dump, string id) => dump.Boards.Single().Items.Single(item => item.Id == id);

    [Test]
    public async Task Fetch_WritesTheDumpLayoutAndReaderLoadsItBack()
    {
        // Arrange
        var update = FakeMondayApi.Update("u1", "2026-11-03T00:00:00Z", new JsonArray(
            FakeMondayApi.Reply("r2", "2026-11-03T02:00:00Z"), FakeMondayApi.Reply("r1", "2026-11-03T01:00:00Z")));
        var older = FakeMondayApi.Update("u0", "2026-11-02T00:00:00Z");
        var subitem = FakeMondayApi.Item("s1", "Example subtask", string.Empty, status: "Todo");
        _monday.Items.Add(FakeMondayApi.Item("10", "Example 項目一", "group_a", new JsonArray(update, older), new JsonArray(subitem)));
        _monday.Items.Add(FakeMondayApi.Item("11", "Example 項目二", "group_m11", status: null));
        _monday.Items.Add(FakeMondayApi.Item("12", "Example 項目三", "group_a", status: "Stuck"));

        // Act
        var summary = await FetchAsync();
        var dump = MondayDumpReader.Load(Dump);

        // Assert
        var board = dump.Boards.Single();
        var first = ItemById(dump, "10");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(Dump, "meta.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(Dump, "users.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(Dump, "boards", "123.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(Dump, "boards", "123-subitems.json")), Is.True);
            Assert.That(Directory.GetFiles(Path.Combine(Dump, "items", "123")).Select(Path.GetFileName).Order(), Is.EqualTo(new[] { "0001.json", "0002.json" }));
            Assert.That(summary.Boards.Single().Items, Is.EqualTo(3));
            Assert.That(summary.Boards.Single().Subitems, Is.EqualTo(1));
            Assert.That(summary.Boards.Single().Pages, Is.EqualTo(2));
            Assert.That(dump.Users.Select(user => user.Id), Is.EqualTo(new[] { "1001", "1002", "1003" }));
            Assert.That(dump.Users.Single(user => user.Id == "1003").Email, Is.Null);
            Assert.That(board.Name, Is.EqualTo("Example Cases"));
            Assert.That(board.Groups.Select(group => group.Id), Is.EqualTo(new[] { "group_a", "group_m11", "group_old" }));
            Assert.That(board.Items.Select(item => item.Id), Is.EqualTo(new[] { "10", "11", "12" }));
            Assert.That(dump.FetchedAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5)));
            Assert.That(first.Updates.Select(item => item.Id), Is.EqualTo(new[] { "u0", "u1" }), "留言由舊到新");
            Assert.That(first.Updates[1].Replies.Select(reply => reply.Id), Is.EqualTo(new[] { "r1", "r2" }), "回覆由舊到新");
            Assert.That(first.Subitems.Single().GroupId, Is.Empty);
            Assert.That(first.Subitems.Single().Values.First(value => value.ColumnId == "status").Label, Is.EqualTo("Todo"));
            Assert.That(first.GroupId, Is.EqualTo("group_a"));
            Assert.That(first.State, Is.EqualTo("active"));
            Assert.That(first.CreatorId, Is.EqualTo("1001"));
            Assert.That(first.Url, Does.EndWith("/pulses/10"));
            Assert.That(first.CreatedAt, Is.EqualTo(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
            Assert.That(ItemById(dump, "11").Values.First(value => value.ColumnId == "status").Label, Is.Null);
        }
    }

    [Test]
    public async Task Reader_ParsesColumnsLabelsPeopleAndLinkedItems()
    {
        // Arrange
        _monday.Items.Add(FakeMondayApi.Item("10", "Example", "group_a"));

        // Act
        await FetchAsync();
        var board = MondayDumpReader.Load(Dump).Boards.Single();

        // Assert
        var status = board.Columns.Single(column => column.Id == "status");
        var item = board.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.Labels, Is.EqualTo(new[] { "Working on it", "Done", "Stuck" }), "照 index 排，空的標籤不列");
            Assert.That(board.Columns.Single(column => column.Id == "person").Labels, Is.Empty);
            Assert.That(board.SubitemColumns.Select(column => column.Id), Is.EqualTo(new[] { "status", "person" }));
            Assert.That(board.SubitemColumns[0].Labels, Is.EqualTo(new[] { "Todo", "Done" }));
            Assert.That(item.Values.Single(value => value.ColumnId == "person").PersonIds, Is.EqualTo(new[] { "1002" }), "team 不算人");
            var relation = item.Values.Single(value => value.ColumnId == "client");
            Assert.That(relation.LinkedItems.Select(linked => linked.Name), Is.EqualTo(new[] { "Example, A", "Example B" }));
            Assert.That(item.Values.Single(value => value.ColumnId == "date4").ValueJson, Is.EqualTo("""{"date":"2026-11-04"}"""));
            Assert.That(item.Values.Single(value => value.ColumnId == "date4").Text, Is.EqualTo("2026-11-04"));
        }
    }

    [Test]
    public async Task Fetch_MoreThan100Updates_ReadsTheRestIntoUpdatesFolderAndReaderMergesThem()
    {
        // Arrange
        var first = Enumerable.Range(0, 100).Select(n => FakeMondayApi.Update($"a{n:D3}", $"2026-11-02T00:{n % 60:D2}:00Z")).ToArray();
        _monday.Items.Add(FakeMondayApi.Item("10", "Example", "group_a", new JsonArray([.. first])));
        _monday.ExtraUpdates["10"] = [.. Enumerable.Range(0, 30).Select(n => FakeMondayApi.Update($"b{n:D3}", "2026-11-01T00:00:00Z"))];

        // Act
        await FetchAsync();
        var item = MondayDumpReader.Load(Dump).Boards.Single().Items.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(Dump, "updates", "10.json")), Is.True);
            Assert.That(item.Updates, Has.Count.EqualTo(130));
            Assert.That(item.Updates.Select(update => update.Id).Distinct().Count(), Is.EqualTo(130));
            Assert.That(item.Updates[0].CreatedAt, Is.LessThanOrEqualTo(item.Updates[^1].CreatedAt));
        }
    }

    [Test]
    public async Task Fetch_NeverStoresPublicUrlsOrTheToken()
    {
        // Arrange
        var asset = _monday.Asset("501", "screenshot.png");
        _monday.Items.Add(FakeMondayApi.Item("10", "Example", "group_a", new JsonArray(FakeMondayApi.Update("u1", "2026-11-03T00:00:00Z", assets: new JsonArray(asset.DeepClone())))));

        // Act
        await FetchAsync(files: true);

        // Assert
        var everything = string.Concat(Directory.EnumerateFiles(Dump, "*.json", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.That(everything, Does.Not.Contain("public_url").And.Not.Contain(FakeMondayApi.SecretSignature).And.Not.Contain(FakeMondayApi.Token));
        Assert.That(everything, Does.Not.Contain("complexity"));
    }

    [Test]
    public async Task Fetch_WithFiles_DownloadsAndTheReaderPointsToThem()
    {
        // Arrange
        var itemAsset = _monday.Asset("500", "report.pdf", "pdf-bytes");
        var replyAsset = _monday.Asset("501", "shot.png", "png-bytes");
        var reply = FakeMondayApi.Reply("r1", "2026-11-03T01:00:00Z");
        reply["assets"] = new JsonArray(replyAsset.DeepClone());
        var update = FakeMondayApi.Update("u1", "2026-11-03T00:00:00Z", new JsonArray(reply));
        _monday.Items.Add(FakeMondayApi.Item("10", "Example", "group_a", new JsonArray(update), assets: new JsonArray(itemAsset.DeepClone())));

        // Act
        var summary = await FetchAsync(files: true);
        var item = MondayDumpReader.Load(Dump).Boards.Single().Items.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.FilesDownloaded, Is.EqualTo(2));
            Assert.That(item.Assets.Single().LocalPath, Is.EqualTo(Path.Combine(Path.GetFullPath(Dump), "files", "500", "report.pdf")));
            Assert.That(File.ReadAllText(item.Assets.Single().LocalPath!), Is.EqualTo("pdf-bytes"));
            Assert.That(item.Updates.Single().Replies.Single().Assets.Single().LocalPath, Does.EndWith(Path.Combine("501", "shot.png")));
            Assert.That(item.Assets.Single().SizeBytes, Is.EqualTo(9));
            Assert.That(item.Assets.Single().UploadedById, Is.EqualTo("1002"));
            Assert.That(File.ReadAllText(Path.Combine(Dump, "files", "skipped.json")).Trim(), Is.EqualTo("[]"));
            Assert.That(_server.Requests.Where(request => request.Method == HttpMethod.Get).All(request => request.Header("Authorization") is null), Is.True, "下載不帶 monday 的權杖");
        }
    }

    [Test]
    public async Task Fetch_FileOver25MiBOrFailingDownload_IsSkippedAndRecorded()
    {
        // Arrange
        var huge = _monday.Asset("600", "video.mp4", declaredSize: 30L * 1024 * 1024, downloadable: false);
        var broken = _monday.Asset("601", "gone.png", downloadable: false);
        _server.On("GET", "/f/601", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        _monday.Items.Add(FakeMondayApi.Item("10", "Example", "group_a", assets: new JsonArray(huge.DeepClone(), broken.DeepClone())));

        // Act
        var summary = await FetchAsync(files: true);
        var skipped = JsonNode.Parse(File.ReadAllText(Path.Combine(Dump, "files", "skipped.json")))!.AsArray();
        var item = MondayDumpReader.Load(Dump).Boards.Single().Items.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.FilesSkipped.Select(file => file.Reason), Is.EquivalentTo(new[] { "too_large", "download_failed" }));
            Assert.That(skipped.Select(entry => entry!["assetId"]!.GetValue<string>()), Is.EquivalentTo(new[] { "600", "601" }));
            Assert.That(item.Assets.All(asset => asset.LocalPath is null), Is.True);
            Assert.That(_server.To("GET", "/f/600"), Is.Empty);
        }
    }

    [Test]
    public async Task Fetch_Twice_ReusesFilesWithTheSameIdAndSize_AndKeepsThemWithoutFilesOption()
    {
        // Arrange
        var asset = _monday.Asset("500", "report.pdf", "pdf-bytes");
        _monday.Items.Add(FakeMondayApi.Item("10", "Example", "group_a", assets: new JsonArray(asset)));
        await FetchAsync(files: true);
        var downloadsAfterFirst = _server.To("GET", "/f/500").Count;

        // Act
        var second = await FetchAsync(files: true);
        var third = await FetchAsync(files: false);
        var item = MondayDumpReader.Load(Dump).Boards.Single().Items.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(downloadsAfterFirst, Is.EqualTo(1));
            Assert.That(second.FilesReused, Is.EqualTo(1));
            Assert.That(second.FilesDownloaded, Is.Zero);
            Assert.That(_server.To("GET", "/f/500"), Has.Count.EqualTo(1));
            Assert.That(third.FilesRequested, Is.False);
            Assert.That(item.Assets.Single().LocalPath, Is.Not.Null, "沒給 --files 時，上次下載的檔案留著");
        }
    }

    [Test]
    public async Task Fetch_WithMapping_AsksOnlyForTheGroupsThatAreNotSkipped()
    {
        // Arrange
        _monday.Items.Add(FakeMondayApi.Item("10", "Example", "group_a"));
        var mapping = new MondayMapping
        {
            Dump = Dump,
            Boards = new Dictionary<string, BoardMapping>
            {
                [FakeMondayApi.BoardId] = new()
                {
                    Groups = new Dictionary<string, GroupMapping>
                    {
                        ["group_a"] = new() { Project = "CASE", Type = "task" },
                        ["group_m11"] = new() { Project = "MNT", Type = "change" },
                        ["group_old"] = new() { Skip = true },
                    },
                },
            },
        };

        // Act
        await FetchAsync(mapping: mapping);

        // Assert
        var rules = _monday.Variables.Single(variables => variables["query"] is not null)["query"]!["rules"]![0]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rules["column_id"]!.GetValue<string>(), Is.EqualTo("group"));
            Assert.That(rules["compare_value"]!.AsArray().Select(value => value!.GetValue<string>()), Is.EqualTo(new[] { "group_a", "group_m11" }));
            Assert.That(rules["operator"]!.GetValue<string>(), Is.EqualTo("any_of"));
            var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(Dump, "meta.json")))!;
            Assert.That(meta["groups"]!["123"]!.AsArray(), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Fetch_WithoutMapping_SendsNoGroupFilter()
    {
        // Arrange
        _monday.Items.Add(FakeMondayApi.Item("10", "Example", "group_a"));

        // Act
        await FetchAsync();

        // Assert
        Assert.That(_monday.Variables.Any(variables => variables["query"] is not null), Is.False);
        Assert.That(JsonNode.Parse(File.ReadAllText(Path.Combine(Dump, "meta.json")))!["groups"]!["123"], Is.Null);
    }

    [Test]
    public async Task Fetch_FailingHalfway_KeepsThePreviousDumpAndLeavesNoTemporaryFolder()
    {
        // Arrange
        _monday.Items.Add(FakeMondayApi.Item("10", "Example 一", "group_a"));
        await FetchAsync();
        _monday.Items.AddRange([FakeMondayApi.Item("11", "Example 二", "group_a"), FakeMondayApi.Item("12", "Example 三", "group_a")]);
        _monday.FailOnCursor = "c2";

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() => FetchAsync());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Message, Does.Contain("連不上 monday"));
            Assert.That(MondayDumpReader.Load(Dump).Boards.Single().Items, Has.Count.EqualTo(1), "舊的 dump 原封不動");
            Assert.That(Directory.GetDirectories(_root), Is.EqualTo(new[] { Path.Combine(_root, "dump") }));
        }
    }

    [Test]
    public async Task Fetch_IntoAFolderThatIsNotADump_Refuses()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_root, "precious"));
        File.WriteAllText(Path.Combine(_root, "precious", "notes.txt"), "keep me");
        _monday.Items.Add(FakeMondayApi.Item("10", "Example", "group_a"));

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() => FetchAsync(output: Path.Combine(_root, "precious")));

        // Assert
        Assert.That(failure!.Message, Does.Contain("不是 kumi 抓的 dump"));
        Assert.That(File.ReadAllText(Path.Combine(_root, "precious", "notes.txt")), Is.EqualTo("keep me"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Fetch_UnknownBoard_SaysSo()
    {
        // Arrange
        _monday.Items.Add(FakeMondayApi.Item("10", "Example", "group_a"));
        _monday.Attach(_server);
        _server.On("POST", "/v2", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "data": { "users": [], "boards": [], "complexity": { "before": 1, "after": 1, "query": 1, "reset_in_x_seconds": 1 } } }"""),
        });
        var http = new HttpClient(_server);
        var client = new MondayGraphQlClient(http, FakeMondayApi.Url, FakeMondayApi.Token, (_, _) => Task.CompletedTask);

        // Act
        var failure = Assert.ThrowsAsync<CliException>(() =>
            new MondayFetcher(client, http, _progress).RunAsync(new MondayFetchOptions(["999"], Dump, null, false), CancellationToken.None));

        // Assert
        Assert.That(failure!.Message, Does.Contain("找不到看板 999"));
        await Task.CompletedTask;
    }

    [Test]
    public void Reader_NotADump_SaysToRunFetch()
    {
        // Arrange
        Directory.CreateDirectory(Dump);

        // Act
        var failure = Assert.Throws<CliException>(() => MondayDumpReader.Load(Dump));

        // Assert
        Assert.That(failure!.Message, Does.Contain("kumi import monday fetch"));
    }

    [TestCase("a/b\\evil.txt", "evil.txt")]
    [TestCase("..", "file")]
    [TestCase("report?.pdf", "report_.pdf")]
    [TestCase("  .hidden  ", "hidden")]
    public void SafeFileName_StripsPathsAndForbiddenCharacters(string name, string expected) =>
        Assert.That(MondayDumpLayout.SafeFileName(name), Is.EqualTo(expected));

    [Test]
    public void SafeFileName_LongNames_KeepTheExtension() =>
        Assert.That(MondayDumpLayout.SafeFileName(new string('a', 300) + ".pdf"), Has.Length.EqualTo(200).And.EndsWith(".pdf"));

    [Test]
    public void SafeFileName_LongChineseNames_StayWithinTheFileSystemByteLimit_AndKeepTheExtension()
    {
        var name = MondayDumpLayout.SafeFileName(new string('長', 300) + ".pdf");

        Assert.That(System.Text.Encoding.UTF8.GetByteCount(name), Is.LessThanOrEqualTo(200));
        Assert.That(name, Does.EndWith(".pdf").And.StartWith("長"));
    }
}
