using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Import.Monday;
using PmTool.Cli.Import.Monday.Run;

namespace PmTool.Cli.Tests;

/// <summary>kumi import monday run：整個流程對假的 monday 與假的 Kumi 伺服器走一次（交接第 6 節）。</summary>
[TestFixture]
public sealed class MondayRunCommandTests
{
    private const string MappingYaml = """
        source: monday
        dump: ./dump
        people:
          "1001": pm@example.com
          "1002": dev@example.com
          "1003": null
        fallbackPerson: former@example.com
        assignee:
          exclude: ["1001"]
        boards:
          "123":
            statusColumn: status
            ownerColumn: person
            dueColumn: date4
            states: { "Done": done, "Working on it": doing, "Stuck": doing, "": todo }
            fields:
              client:
                field: client
                values: { "Example, A": client_a, "Example B": client_b }
            linkField: monday_url
            subitems: { type: task, statusColumn: status, ownerColumn: person }
            groups:
              group_a: { project: CASE, type: task }
              group_m11:
                project: MNT
                type: change
                cycle: { name: "2026 年 11 月", start: 2026-11-01, end: 2026-11-30 }
              group_old: skip
        """;

    private CliHarness _cli = null!;
    private FakeMondayApi _monday = null!;
    private FakePmBackend _backend = null!;

    [SetUp]
    public async Task SetUp()
    {
        _cli = new CliHarness().LoggedInByEnvironment();
        _cli.Environment["MONDAY_API_TOKEN"] = FakeMondayApi.Token;
        _cli.Environment["MONDAY_API_URL"] = FakeMondayApi.Url;
        _backend = new FakePmBackend();
        _backend.Attach(_cli.Server);
        _monday = new FakeMondayApi();
        var body = "<p>請 <a class=\"user_mention_editor router\" data-mention-id=\"1002\" data-mention-type=\"User\">@Dev</a> 看圖"
            + "<img data-asset_id=\"501\" src=\"https://files.example.test/x?sig=1\"><img data-asset_id=\"503\"></p>";
        var first = FakeMondayApi.Update(
            "u1", "2026-11-02T03:00:00Z", new JsonArray(FakeMondayApi.Reply("r1", "2026-11-02T04:00:00Z", "1003")),
            new JsonArray(_monday.Asset("501", "shot.png", "png-bytes"), _monday.Asset("502", "spec.pdf", "pdf-bytes"), _monday.Asset("503", "missing.png", downloadable: false)));
        first["body"] = body;
        var second = FakeMondayApi.Update("u2", "2026-11-03T03:00:00Z", creator: "1002");
        var subitem = FakeMondayApi.Item("s1", "Example 子項目", string.Empty, status: "Done");
        _monday.Items.Add(FakeMondayApi.Item(
            "10", "Example 項目一", "group_a", new JsonArray(first, second), new JsonArray(subitem), new JsonArray(_monday.Asset("700", "notes.txt", "notes")), "Done"));
        _monday.Items.Add(FakeMondayApi.Item("11", "Example 項目二", "group_m11", new JsonArray(FakeMondayApi.Update("u9", "2026-11-04T00:00:00Z")), status: "Stuck"));
        _monday.Attach(_cli.Server);
        _cli.Server.On("GET", "/f/503", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await File.WriteAllTextAsync(MappingPath, MappingYaml);
        await FetchAsync();
    }

    [TearDown]
    public void TearDown() => _cli.Dispose();

    private string MappingPath => _cli.PathOf("monday.yaml");

    private async Task FetchAsync()
    {
        var result = await _cli.RunAsync("import", "monday", "fetch", "--board", "123", "--out", _cli.PathOf("dump"), "--files");
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
    }

    private Task<CliResult> RunAsync(params string[] extra) =>
        _cli.RunAsync(["import", "monday", "run", "--mapping", MappingPath, .. extra]);

    private IReadOnlyList<RecordedRequest> Writes() =>
        [.. _cli.Server.Requests.Where(request => request.Path.StartsWith("/v1", StringComparison.Ordinal) && request.Method != HttpMethod.Get && request.Path != "/v1/history/lookup")];

    private List<string> Order() => [.. Writes().Select(request => $"{request.Method} {request.Path}")];

    [Test]
    public async Task Run_Dry_ChecksEverythingAndAsksTheServerRowByRow_ButWritesNothing()
    {
        // Act
        var result = await RunAsync("--dry-run");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Out + result.Error);
            Assert.That(result.Out, Does.Contain("乾跑").And.Contain("專案 CASE").And.Contain("項目：會新增 2，已經有 0").And.Contain("週期：會建立 「2026 年 11 月」"));
            Assert.That(Order(), Is.EqualTo(new[] { "POST /v1/projects/CASE/history/items", "POST /v1/projects/MNT/history/items" }));
            Assert.That(Writes().All(request => request.Json!["dryRun"]!.GetValue<bool>()), Is.True);
            Assert.That(_backend.Items, Is.Empty);
            Assert.That(_backend.Cycles["MNT"], Is.Empty);
            Assert.That(_backend.Attachments, Is.Empty);
        }
    }

    [Test]
    public async Task Run_Dry_ServerRowProblems_AreListedWithTheItemAndExitNonZero()
    {
        // Arrange
        _backend.RowProblem = row => row["title"]!.GetValue<string>() == "Example 項目二" ? "欄位的值不合規定。" : null;

        // Act
        var result = await RunAsync("--dry-run");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Out, Does.Contain("專案 MNT：項目「Example 項目二」（monday 的 11）欄位的值不合規定。（rows/0/fields）"));
        Assert.That(_backend.Items, Is.Empty);
    }

    [Test]
    public async Task Run_Real_WritesCyclesItemsAttachmentsAndCommentsInOrder_WithTheOriginalAuthorsAndTimes()
    {
        // Act
        var result = await RunAsync();

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Out + result.Error);
        Assert.That(Order(), Is.EqualTo(new[]
        {
            "POST /v1/projects/MNT/cycles",
            "POST /v1/projects/CASE/history/items", "POST /v1/projects/MNT/history/items",
            "POST /v1/items/CASE-1/history/attachments", "POST /v1/items/CASE-1/history/attachments", "POST /v1/items/CASE-1/history/attachments",
            "POST /v1/projects/CASE/history/comments", "POST /v1/projects/MNT/history/comments",
        }));
        var caseRows = _cli.Server.To("POST", "/v1/projects/CASE/history/items").Single().Json!["rows"]!.AsArray();
        var maintenanceRow = _cli.Server.To("POST", "/v1/projects/MNT/history/items").Single().Json!["rows"]![0]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(caseRows.Select(row => row!["externalId"]!.GetValue<string>()), Is.EqualTo(new[] { "10", "s1" }));
            Assert.That(caseRows[0]!["assigneeId"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Dev.ToString()));
            Assert.That(caseRows[0]!["fields"]!["client"]!.GetValue<string>(), Is.EqualTo("client_a"));
            Assert.That(caseRows[0]!["fields"]!["monday_url"]!.GetValue<string>(), Does.EndWith("/pulses/10"));
            Assert.That(caseRows[1]!["parentExternalId"]!.GetValue<string>(), Is.EqualTo("10"));
            Assert.That(maintenanceRow["cycleId"]!.GetValue<string>(), Is.EqualTo(_backend.Cycles["MNT"].Single().Id.ToString()));
            Assert.That(maintenanceRow["state"]!.GetValue<string>(), Is.EqualTo("doing"));
            Assert.That(_cli.Server.To("POST", "/v1/projects/CASE/history/items").Single().Json!["source"]!.GetValue<string>(), Is.EqualTo("monday"));
        }
    }

    [Test]
    public async Task Run_Real_UploadsFilesWithTheFieldsInOrder_AndTheUploaderAndTimeOfTheOriginal()
    {
        // Act
        await RunAsync();

        // Assert
        var uploads = _cli.Server.Requests.Where(request => request.Path.EndsWith("/history/attachments", StringComparison.Ordinal)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_backend.UploadFieldOrders, Has.Count.EqualTo(3));
            Assert.That(_backend.UploadFieldOrders.All(order => order.SequenceEqual(new[] { "source", "externalId", "uploadedBy", "uploadedAt", "file" })), Is.True);
            Assert.That(uploads[0].Body, Does.Contain("700").And.Contain(FakePmBackend.Dev.ToString()).And.Contain("2026-11-02T03:04:05+00:00").And.Contain("notes"));
            Assert.That(uploads[1].Body, Does.Contain("501").And.Contain(FakePmBackend.Pm.ToString()).And.Contain("2026-11-02T03:00:00+00:00"));
            Assert.That(_backend.Attachments.Keys, Is.EquivalentTo(new[] { "700", "501", "502" }), "沒下載的 503 不上傳");
        }
    }

    [Test]
    public async Task Run_Real_CommentsKeepAuthorsAndTimes_RepliesFollowTheirParent_AndFilesAreInlinedOrListed()
    {
        // Act
        var result = await RunAsync();

        // Assert
        var comments = _cli.Server.To("POST", "/v1/projects/CASE/history/comments").Single().Json!["comments"]!.AsArray();
        var first = comments[0]!;
        var reply = comments[1]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(comments.Select(comment => comment!["externalId"]!.GetValue<string>()), Is.EqualTo(new[] { "u1", "r1", "u2" }));
            Assert.That(first["authorId"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Pm.ToString()));
            Assert.That(first["at"]!.GetValue<string>(), Is.EqualTo("2026-11-02T03:00:00+00:00"));
            Assert.That(first["itemExternalId"]!.GetValue<string>(), Is.EqualTo("10"));
            Assert.That(first["body"]!.GetValue<string>(), Does.Contain($"@[Example Dev](mention:{FakePmBackend.Dev})")
                .And.Contain($"![shot.png](attachment:{_backend.Attachments["501"].Id})")
                .And.Contain("［圖片：missing.png，沒有搬過來，見 monday 原連結］")
                .And.EndWith($"[spec.pdf](attachment:{_backend.Attachments["502"].Id})"));
            Assert.That(reply["replyToExternalId"]!.GetValue<string>(), Is.EqualTo("u1"));
            Assert.That(reply["authorId"]!.GetValue<string>(), Is.EqualTo(FakePmBackend.Former.ToString()));
            Assert.That(reply["body"]!.GetValue<string>(), Is.EqualTo("（原作者：Former Staff）\n\nreply r1"));
            Assert.That(comments[2]!.AsObject().ContainsKey("replyToExternalId"), Is.False);
            Assert.That(result.Out, Does.Contain("沒有搬過來").And.Contain("missing.png").And.Contain("Former Staff 對不上這裡的人，留言改用 fallbackPerson"));
        }
    }

    [Test]
    public async Task Run_Real_PrintsTheReportPerProject()
    {
        // Act
        var result = await RunAsync();

        // Assert
        Assert.That(result.Out, Does.Contain("匯入完成。")
            .And.Contain("專案 CASE\n  項目：新增 2，略過 0")
            .And.Contain("留言：新增 3，略過 0")
            .And.Contain("附件：新增 3，略過 0，沒搬 1")
            .And.Contain("專案 MNT\n  項目：新增 1，略過 0\n  留言：新增 1，略過 0")
            .And.Contain("週期：建立 「2026 年 11 月」"));
        Assert.That(result.Error, Does.Contain("專案 CASE：項目 2/2").Or.Contain("專案"));
    }

    [Test]
    public async Task Run_ItemFileUploadedByAnUnmatchedPerson_IsAttributedToTheItemCreator()
    {
        // Arrange
        var asset = _monday.Asset("701", "old.txt", "old");
        asset["uploaded_by"] = FakeMondayApi.Parse("""{ "id": "1003" }""");
        ((JsonArray)_monday.Items[1]["assets"]!).Add(asset);
        _monday.Attach(_cli.Server);
        await FetchAsync();

        // Act
        await RunAsync();

        // Assert
        var upload = _cli.Server.Requests.Single(request => request.Path.EndsWith("/history/attachments", StringComparison.Ordinal) && request.Body.Contains("701"));
        Assert.That(upload.Body, Does.Contain(FakePmBackend.Pm.ToString()).And.Not.Contain(FakePmBackend.Former.ToString()));
    }

    [Test]
    public async Task Run_AddMembers_SendsTheEditorRoleByDefault_AndTheRoleGivenWithMemberRole()
    {
        // Arrange
        _backend.Members["CASE"].Remove(FakePmBackend.Former);
        _backend.Members["MNT"].Remove(FakePmBackend.Former);

        // Act
        var byDefault = await RunAsync("--add-members", "--dry-run");
        var viewer = await RunAsync("--add-members", "--member-role", "viewer");
        var unknown = await RunAsync("--add-members", "--member-role", "intern");

        // Assert
        Assert.That(byDefault.ExitCode, Is.EqualTo(ExitCodes.Success), byDefault.Out);
        Assert.That(viewer.ExitCode, Is.EqualTo(ExitCodes.Success), viewer.Out + viewer.Error);
        Assert.That(_backend.MemberRoles, Is.EqualTo(new[] { "viewer" }));
        Assert.That(unknown.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(unknown.Out, Does.Contain("專案 CASE 的藍圖裡沒有角色「intern」"));
    }

    [Test]
    public async Task Run_Again_AfterTheProjectWasArchived_SkipsItWhenThereIsNothingNew()
    {
        // Arrange：匯完之後把做完的案子封存（搬家時的做法），切換期間再跑一次
        Assert.That((await RunAsync()).ExitCode, Is.EqualTo(ExitCodes.Success));
        _backend.ArchivedProjects.Add("CASE");
        var before = Writes().Count;

        // Act
        var result = await RunAsync();

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Out + result.Error);
        Assert.That(result.Out, Does.Contain("專案 CASE 已經封存，沒有新的東西，略過"));
        Assert.That(Writes(), Has.Count.EqualTo(before));
    }

    [Test]
    public async Task Run_Again_AfterTheProjectWasArchived_StopsBeforeWritingWhenThereIsSomethingNewForIt()
    {
        // Arrange：封存之後，monday 上那個項目又多了一則留言
        Assert.That((await RunAsync()).ExitCode, Is.EqualTo(ExitCodes.Success));
        _backend.ArchivedProjects.Add("CASE");
        ((JsonArray)_monday.Items[0]["updates"]!).Add(FakeMondayApi.Update("u3", "2026-11-05T00:00:00Z"));
        await FetchAsync();
        var before = Writes().Count;

        // Act
        var result = await RunAsync();

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid), result.Out + result.Error);
        Assert.That(result.Out + result.Error, Does.Contain("專案 CASE 已經封存").And.Contain("1 則新的留言").And.Contain("取消封存"));
        Assert.That(Writes(), Has.Count.EqualTo(before));
    }

    [Test]
    public async Task Run_AddMembers_AcceptsACustomRoleFromTheBlueprint()
    {
        // Arrange：藍圖只列自訂角色（client），內建的角色不在藍圖裡
        _backend.Members["CASE"].Remove(FakePmBackend.Former);

        // Act
        var result = await RunAsync("--add-members", "--member-role", "client");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Out + result.Error);
        Assert.That(_backend.MemberRoles, Is.EqualTo(new[] { "client" }));
    }

    [Test]
    public async Task Run_AddMembers_UsesEditorWhenNoRoleIsGiven()
    {
        // Arrange
        _backend.Members["CASE"].Remove(FakePmBackend.Former);

        // Act
        await RunAsync("--add-members");

        // Assert
        Assert.That(_backend.MemberRoles, Is.EqualTo(new[] { "editor" }));
    }

    [Test]
    public async Task Run_FilesNotMoved_SayWhyFromTheDumpsSkippedList()
    {
        // Act
        var result = await RunAsync();
        var skipped = MondayDumpReader.Load(_cli.PathOf("dump")).SkippedFiles;

        // Assert
        Assert.That(skipped, Is.EqualTo(new Dictionary<string, string> { ["503"] = "download_failed" }));
        Assert.That(result.Out, Does.Contain("檔案「missing.png」（CASE-1）沒有搬過來：fetch 下載失敗"));
    }

    [Test]
    public async Task Fetch_OutWithATrailingSlash_StillReplacesTheFolder()
    {
        // Act
        var result = await _cli.RunAsync("import", "monday", "fetch", "--board", "123", "--out", _cli.PathOf("dump") + "/", "--files");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(MondayDumpReader.Load(_cli.PathOf("dump")).Boards.Single().Items, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Fetch_AssetWithAnImpossiblePath_IsRecordedAsAnIoErrorAndTheRestIsFetched()
    {
        // Arrange
        var long_ = _monday.Asset(new string('9', 300), "x.txt", "x");
        ((JsonArray)_monday.Items[1]["assets"]!).Add(long_);
        _monday.Attach(_cli.Server);

        // Act
        await FetchAsync();

        // Assert
        var skipped = MondayDumpReader.Load(_cli.PathOf("dump")).SkippedFiles;
        Assert.That(skipped[new string('9', 300)], Is.EqualTo("io_error"));
        Assert.That(skipped.ContainsKey("503"), Is.True);
    }

    [Test]
    public async Task Run_AttachmentRejectedForTheFileItself_IsNotMoved_ButOtherRejectionsFailTheRun()
    {
        // Arrange
        _backend.AttachmentFailure = (HttpStatusCode.UnprocessableEntity, "attachment.too_large");

        // Act
        var tooLarge = await RunAsync();
        _backend.AttachmentFailure = (HttpStatusCode.UnprocessableEntity, "history.invalid");
        var invalid = await RunAsync();

        // Assert
        Assert.That(tooLarge.ExitCode, Is.EqualTo(ExitCodes.Success), tooLarge.Out + tooLarge.Error);
        Assert.That(tooLarge.Out, Does.Contain("伺服器拒絕：伺服器不收這個檔案。").And.Contain("附件：新增 0，略過 0，沒搬 4"));
        Assert.That(invalid.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(invalid.Out, Does.Contain("匯入中途失敗"));
    }

    [Test]
    public async Task Run_ItemFileAlsoInAComment_IsUploadedOnceAndAttributedToTheComment()
    {
        // Arrange
        var duplicate = _monday.Asset("501", "shot.png", "png-bytes");
        ((JsonArray)_monday.Items[0]["assets"]!).Add(duplicate);
        _monday.Attach(_cli.Server);
        await FetchAsync();

        // Act
        await RunAsync();

        // Assert
        var uploads = _cli.Server.Requests.Where(request => request.Path.EndsWith("/history/attachments", StringComparison.Ordinal) && request.Body.Contains("shot.png")).ToArray();
        Assert.That(uploads, Has.Length.EqualTo(1));
        Assert.That(uploads[0].Body, Does.Contain(FakePmBackend.Pm.ToString()).And.Contain("2026-11-02T03:00:00+00:00"));
        Assert.That(_backend.UploadFieldOrders, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Run_Twice_TheSecondTimeSkipsEverythingAndDoesNotUploadAgain()
    {
        // Arrange
        await RunAsync();
        var requestsBefore = _cli.Server.Requests.Count;
        var uploadsBefore = _backend.UploadFieldOrders.Count;

        // Act
        var result = await RunAsync();

        // Assert
        var again = _cli.Server.Requests.Skip(requestsBefore).Where(request => request.Method != HttpMethod.Get && request.Path != "/v1/history/lookup").ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Out + result.Error);
            Assert.That(again, Is.Empty, "重跑沒有任何寫入：項目、留言、檔案都已經匯入過");
            Assert.That(_backend.UploadFieldOrders, Has.Count.EqualTo(uploadsBefore));
            Assert.That(result.Out, Does.Contain("項目：新增 0，略過 2").And.Contain("留言：新增 0，略過 3").And.Contain("附件：新增 0，略過 1，沒搬 0"));
        }
    }

    [Test]
    public async Task Run_AfterANewFetch_OnlyTheNewCommentIsAdded()
    {
        // Arrange
        await RunAsync();
        ((JsonArray)_monday.Items[0]["updates"]!).Add(FakeMondayApi.Update("u3", "2026-11-05T00:00:00Z", creator: "1002"));
        await FetchAsync();
        var requestsBefore = _cli.Server.Requests.Count;

        // Act
        var result = await RunAsync();

        // Assert
        var again = _cli.Server.Requests.Skip(requestsBefore).Where(request => request.Method != HttpMethod.Get && request.Path != "/v1/history/lookup").ToArray();
        Assert.That(again.Select(request => request.Path), Is.EqualTo(new[] { "/v1/projects/CASE/history/comments" }));
        Assert.That(again[0].Json!["comments"]!.AsArray().Select(comment => comment!["externalId"]!.GetValue<string>()), Is.EqualTo(new[] { "u3" }));
        Assert.That(result.Out, Does.Contain("留言：新增 1，略過 3"));
    }

    [Test]
    public async Task Run_WithMissingMembers_ListsThemAndWritesNothing_UnlessAddMembersIsGiven()
    {
        // Arrange
        _backend.Members["CASE"].Remove(FakePmBackend.Former);

        // Act
        var without = await RunAsync();
        var writesWithout = Writes().Count;
        var adding = await RunAsync("--add-members");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(without.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(without.Out, Does.Contain("專案 CASE 要有這些人當成員：Former Staff"));
            Assert.That(writesWithout, Is.Zero);
            Assert.That(adding.ExitCode, Is.EqualTo(ExitCodes.Success), adding.Out + adding.Error);
            Assert.That(adding.Out, Does.Contain("成員：加入 Former Staff"));
            var order = Order();
            Assert.That(order.IndexOf($"PUT /v1/projects/CASE/members/{FakePmBackend.Former}"), Is.LessThan(order.IndexOf("POST /v1/projects/CASE/history/items")).And.GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public async Task Run_Dry_WithMembersToAdd_DoesNotAddThemAndSaysTheServerCheckWasSkipped()
    {
        // Arrange
        _backend.Members["CASE"].Remove(FakePmBackend.Former);

        // Act
        var result = await RunAsync("--dry-run", "--add-members");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Out + result.Error);
        Assert.That(result.Out, Does.Contain("成員：會加入 Former Staff").And.Contain("專案 CASE 還有成員要加，伺服器的逐列檢查略過"));
        Assert.That(Writes().All(request => request.Path != "/v1/projects/CASE/members/" + FakePmBackend.Former), Is.True);
    }

    [Test]
    public async Task Run_WithProblems_ListsThemAllAtOnceAndWritesNothing()
    {
        // Arrange
        var broken = MappingYaml
            .Replace("\"Done\": done, ", string.Empty, StringComparison.Ordinal)
            .Replace("field: client", "field: nope", StringComparison.Ordinal)
            .Replace("type: change", "type: nothing", StringComparison.Ordinal)
            .Replace("statusColumn: status\n", "statusColumn: missing_col\n", StringComparison.Ordinal);
        await File.WriteAllTextAsync(MappingPath, broken);

        // Act
        var result = await RunAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Out, Does.Contain("檢查沒有通過，什麼都沒有寫入。").And.Contain("欄 missing_col 不在 dump 裡")
                .And.Contain("沒有項目類型「nothing」").And.Contain("欄位「nope」在所有目標"));
            Assert.That(Writes(), Is.Empty);
        }
    }

    [Test]
    public async Task Run_WhenTheDumpIsMissing_IsReportedAsAnIssue()
    {
        // Arrange
        await File.WriteAllTextAsync(MappingPath, MappingYaml.Replace("./dump", "./nowhere", StringComparison.Ordinal));

        // Act
        var result = await RunAsync();

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Out, Does.Contain("不是 dump 資料夾"));
    }

    [Test]
    public async Task Run_WhenTheServerFailsMidway_PrintsWhatWasDone_ExitsNonZero_AndTheNextRunContinues()
    {
        // Arrange
        _backend.FailComments = true;

        // Act
        var failed = await RunAsync();
        _backend.FailComments = false;
        var resumed = await RunAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.Not.EqualTo(ExitCodes.Success));
            Assert.That(failed.Out, Does.Contain("匯入中途失敗").And.Contain("項目：新增 2，略過 0").And.Contain("附件：新增 3"));
            Assert.That(failed.Error, Does.Contain("伺服器出錯了。"));
            Assert.That(resumed.ExitCode, Is.EqualTo(ExitCodes.Success), resumed.Out + resumed.Error);
            Assert.That(resumed.Out, Does.Contain("項目：新增 0，略過 2").And.Contain("留言：新增 3，略過 0").And.Contain("附件：新增 0，略過 3"));
            Assert.That(_backend.Comments.Keys, Is.EquivalentTo(new[] { "u1", "r1", "u2", "u9" }));
            Assert.That(_backend.UploadFieldOrders, Has.Count.EqualTo(3), "檔案不重傳");
        }
    }

    [Test]
    public async Task Run_ItemAlreadyImportedIntoAnotherProject_IsSkippedAndTheReportSaysWhereItIs()
    {
        // Arrange
        _backend.Items["11"] = ("CASE", "CASE-7");

        // Act
        var result = await RunAsync();

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Out + result.Error);
        Assert.That(result.Out, Does.Contain("monday 的 11 已經在 CASE-7（別的專案），這裡略過"));
        Assert.That(_cli.Server.To("POST", "/v1/projects/MNT/history/items"), Is.Empty);
        Assert.That(_cli.Server.To("POST", "/v1/projects/MNT/history/comments"), Is.Empty);
    }

    [Test]
    public async Task Run_LongComment_IsTruncatedWithANotice_AndRemindsInTheReport()
    {
        // Arrange
        var update = FakeMondayApi.Update("u5", "2026-11-06T00:00:00Z");
        update["body"] = "<p>" + new string('長', 12_000) + "</p>";
        ((JsonArray)_monday.Items[0]["updates"]!).Add(update);
        await FetchAsync();

        // Act
        var result = await RunAsync();

        // Assert
        var body = _backend.Comments["u5"].Json["body"]!.GetValue<string>();
        Assert.That(body.Length, Is.LessThanOrEqualTo(MarkdownText.MaxCommentLength));
        Assert.That(body, Does.EndWith(MarkdownText.TruncationNotice));
        Assert.That(result.Out, Does.Contain("留言 u5").And.Contain("超過 10,000 字，已截斷"));
    }

    [Test]
    public async Task Run_WritesTheReportFile_AndJsonPrintsTheSameThingToStdout()
    {
        // Arrange
        var reportPath = _cli.PathOf("out/report.json");

        // Act
        var result = await RunAsync("--json", "--report", reportPath);

        // Assert
        var fromStdout = JsonNode.Parse(result.Out)!;
        var fromFile = JsonNode.Parse(await File.ReadAllTextAsync(reportPath))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(fromStdout["status"]!.GetValue<string>(), Is.EqualTo("completed"));
            Assert.That(fromStdout["projects"]![0]!["project"]!.GetValue<string>(), Is.EqualTo("CASE"));
            Assert.That(fromStdout["projects"]![0]!["items"]!["created"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(fromStdout["projects"]![0]!["attachments"]!["notMoved"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(fromStdout["reminders"]!.AsArray(), Is.Not.Empty);
            Assert.That(fromFile.ToJsonString(), Is.EqualTo(fromStdout.ToJsonString()));
        }
    }

    [Test]
    public async Task Run_WithoutAMappingOption_IsAUsageError()
    {
        // Act
        var result = await _cli.RunAsync("import", "monday", "run");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--mapping"));
    }
}
