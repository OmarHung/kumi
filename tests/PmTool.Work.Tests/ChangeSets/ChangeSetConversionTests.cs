using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>
/// 欄位換型別：照轉換表逐一改值。一定成功的直接轉；可能失敗的依指定的處理方式：清空（原值封存）或整個擋下。
/// 數字與文字的規則（範圍、小數位數、長度、換行）和直接填值時一樣。
/// </summary>
[TestFixture]
public sealed class ChangeSetConversionTests
{
    private static readonly string LongLine = new('長', WorkRules.MaxTextFieldLength + 1);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ApplyAsync_TextToNumberLeavingFailuresEmpty_ConvertsWhatItCanAndArchivesTheRest()
    {
        // Arrange：WEB-1、WEB-2 轉得過去，WEB-3 不是數字，WEB-4 小數太多位，WEB-5 沒有值。
        var scenario = new Scenario(_host);
        var keys = await SummariesAsync(scenario, "42", " 3.5 ", "abc", "1.23456", null);
        var proposed = await ProposeNumberAsync(scenario, ConversionFailurePolicy.LeaveEmpty);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var values = await SummaryValuesAsync(scenario, keys);
        var archived = await ArchivedValues.ListAsync(_host, scenario);
        var converted = await ActivityLog.LastAsync(_host, scenario.Admin, keys[0]);
        var cleared = await ActivityLog.LastAsync(_host, scenario.Admin, keys[2]);
        var untouched = await ActivityLog.LastAsync(_host, scenario.Admin, keys[4]);
        var number = await _host.Items.UpdateAsync(
            scenario.Admin, keys[4], new WorkItemPatch { Fields = Scenario.Fields(("summary", 7)) }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                proposed.Impacts.Select(i => (i.Impact, i.Affected, i.BlocksApply)),
                Is.EqualTo(new[]
                {
                    ((DataImpact)new FieldConversionImpact("summary", FieldType.Text, FieldType.Number, ConversionFailurePolicy.LeaveEmpty), 4L, false),
                }));
            Assert.That(values, Is.EqualTo(new[] { "42", "3.5", null, null, null }));
            Assert.That(archived, Is.EqualTo(new[] { (3L, "summary", "\"abc\"", 1L), (4L, "summary", "\"1.23456\"", 1L) }));
            Assert.That(ActivityLog.Shapes(converted), Is.EqualTo(new (string, string?, string?)[] { ("summary", "\"42\"", "42") }));
            Assert.That(ActivityLog.Shapes(cleared), Is.EqualTo(new (string, string?, string?)[] { ("summary", "\"abc\"", null) }));
            Assert.That(untouched.Kind, Is.EqualTo(ActivityKind.Created), "沒有值的項目不動");
            Assert.That(number.Succeeded, Is.True, "套用之後欄位是數字");
        }
    }

    [Test]
    public async Task ApplyAsync_TextToNumberAbortingOnFailure_IsBlockedAndChangesNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var keys = await SummariesAsync(scenario, "42", "abc", "1.23456");
        var proposed = await ProposeNumberAsync(scenario, ConversionFailurePolicy.Abort);

        // Act
        var result = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var values = await SummaryValuesAsync(scenario, keys);
        var project = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        var readBack = await _host.Changes.GetAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        const string blocker = "「摘要」有 2 個項目的值不是數字，或超出數字的範圍（WEB-2、WEB-3）。請先改好這些值，或把轉不過去時的處理改成清空。";
        using (Assert.EnterMultipleScope())
        {
            Assert.That((proposed.Impacts[0].Affected, proposed.Impacts[0].BlocksApply, proposed.CanApply), Is.EqualTo((3L, true, false)));
            Assert.That(proposed.Blockers.Select(b => (b.Code, b.Path, b.Message)), Is.EqualTo(new[] { (WorkErrorCodes.ChangeSetConversionFailed, "impacts/0", blocker) }));
            Assert.That((result.Failure!.Kind, result.Failure.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.ChangeSetApplyBlocked)));
            Assert.That(result.Failure.Details, Is.EqualTo(proposed.Blockers));
            Assert.That(values, Is.EqualTo(new[] { "\"42\"", "\"abc\"", "\"1.23456\"" }));
            Assert.That(project.Value.Project.BlueprintVersion, Is.EqualTo(1));
            Assert.That(readBack.Value.ChangeSet.Status, Is.EqualTo(ChangeSetStatus.Pending));
            Assert.That(await ArchivedValues.ListAsync(_host, scenario), Is.Empty);
        }
    }

    [Test]
    public async Task ApplyAsync_MoreFailuresThanListed_NamesTheFirstFewAndCountsTheRest()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await SummariesAsync(scenario, "一", "二", "三", "四", "五", "六", "七");

        // Act
        var proposed = await ProposeNumberAsync(scenario, ConversionFailurePolicy.Abort);

        // Assert
        Assert.That(
            proposed.Blockers.Single().Message,
            Is.EqualTo("「摘要」有 7 個項目的值不是數字，或超出數字的範圍（WEB-1、WEB-2、WEB-3、WEB-4、WEB-5 等 7 個項目）。請先改好這些值，或把轉不過去時的處理改成清空。"));
    }

    // 一定成功的轉換。
    [TestCase("category", FieldType.MultiSelect, "\"bug\"", "[\"bug\"]")]
    [TestCase("summary", FieldType.LongText, "\"一行字\"", "\"一行字\"")]
    [TestCase("story_points", FieldType.Text, "2.5", "\"2.5\"")]
    [TestCase("story_points", FieldType.Text, "100", "\"100\"")]
    [TestCase("review_date", FieldType.Text, "\"2026-11-30\"", "\"2026-11-30\"")]
    [TestCase("reference_url", FieldType.Text, "\"https://example.com/a\"", "\"https://example.com/a\"")]

    // 文字→數字：一般的十進位寫法，範圍與小數位數照數字欄位的規則。
    [TestCase("summary", FieldType.Number, "\"-7\"", "-7")]
    [TestCase("summary", FieldType.Number, "\"+8\"", "8")]
    [TestCase("summary", FieldType.Number, "\".5\"", "0.5")]
    [TestCase("summary", FieldType.Number, "\"5.\"", "5")]
    [TestCase("summary", FieldType.Number, "\"007\"", "7")]
    [TestCase("summary", FieldType.Number, "\"2.50\"", "2.5")]
    [TestCase("summary", FieldType.Number, "\"99999999999.9999\"", "99999999999.9999")]
    [TestCase("summary", FieldType.Number, "\"100000000000\"", null)]
    [TestCase("summary", FieldType.Number, "\"1,200\"", null)]
    [TestCase("summary", FieldType.Number, "\"1e3\"", null)]
    [TestCase("summary", FieldType.Number, "\".\"", null)]
    [TestCase("summary", FieldType.Number, "\"-\"", null)]

    // 文字→日期：只認 YYYY-MM-DD，而且要是真的日期。
    [TestCase("summary", FieldType.Date, "\" 2026-11-30 \"", "\"2026-11-30\"")]
    [TestCase("summary", FieldType.Date, "\"2026/11/30\"", null)]
    [TestCase("summary", FieldType.Date, "\"2026-02-30\"", null)]

    // 多選→單選：只選了一個才轉得過去。
    [TestCase("tags", FieldType.SingleSelect, "[\"docs\"]", "\"docs\"")]
    [TestCase("tags", FieldType.SingleSelect, "[\"docs\",\"backend\"]", null)]

    // 長文字→文字：不能有換行，長度照文字的上限。
    [TestCase("notes", FieldType.Text, "\"一行就好\"", "\"一行就好\"")]
    [TestCase("notes", FieldType.Text, "\"兩\\n行\"", null)]
    public async Task ApplyAsync_ConvertingAValue_FollowsTheConversionTable(string field, FieldType toType, string before, string? after)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "範例任務",
            Fields = new Dictionary<string, System.Text.Json.JsonElement> { [field] = System.Text.Json.JsonDocument.Parse(before).RootElement.Clone() },
        });
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.Field(field, f => Retyped(f, toType)), BlueprintEdits.Converts(field, ConversionFailurePolicy.LeaveEmpty));

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var readBack = (await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None)).Value;

        // Assert
        Assert.That(readBack.Fields.TryGetValue(field, out var value) ? ActivityLog.Text(value) : null, Is.EqualTo(after));
    }

    [TestCase("summary", FieldType.Date, "\"明天\"", "「摘要」有 1 個項目的值不是 YYYY-MM-DD 格式的日期（WEB-1）。")]
    [TestCase("tags", FieldType.SingleSelect, "[\"docs\",\"backend\"]", "「標籤」有 1 個項目的值選了不只一個選項，轉不成單選（WEB-1）。")]
    [TestCase("notes", FieldType.Text, "\"兩\\n行\"", "「備註」有 1 個項目的值有換行或超過 2,000 個字，轉不成單行文字（WEB-1）。")]
    public async Task ProposeAsync_AbortingConversionThatWouldFail_SaysWhyTheValuesDoNotFit(string field, FieldType toType, string value, string reason)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "範例任務",
            Fields = new Dictionary<string, System.Text.Json.JsonElement> { [field] = System.Text.Json.JsonDocument.Parse(value).RootElement.Clone() },
        });

        // Act
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.Field(field, f => Retyped(f, toType)), BlueprintEdits.Converts(field, ConversionFailurePolicy.Abort));

        // Assert
        Assert.That(proposed.Blockers.Single().Message, Is.EqualTo(reason + "請先改好這些值，或把轉不過去時的處理改成清空。"));
    }

    [Test]
    public async Task ApplyAsync_LongTextTooLongForText_IsLeftEmptyAndRecordedWithoutTheContent()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "範例任務", Fields = Scenario.Fields(("notes", LongLine)) });
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.Field("notes", f => Retyped(f, FieldType.Text)), BlueprintEdits.Converts("notes", ConversionFailurePolicy.LeaveEmpty));
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var readBack = (await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None)).Value;
        var migrated = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(readBack.Fields, Is.Empty);
        Assert.That(ActivityLog.Shapes(migrated), Is.EqualTo(new (string, string?, string?)[] { ("notes", null, null) }), "長文字只記有改");
        Assert.That((await ArchivedValues.ListAsync(_host, scenario)).Select(a => a.Value.Length), Is.EqualTo(new[] { LongLine.Length + 2 }));
    }

    /// <summary>換成新的型別；選項只有選項型的欄位才留著。</summary>
    private static FieldDefinition Retyped(FieldDefinition field, FieldType type) =>
        field with { Type = type, Options = type is FieldType.SingleSelect or FieldType.MultiSelect ? field.Options : null };

    private async Task<string[]> SummariesAsync(Scenario scenario, params string?[] summaries)
    {
        await scenario.CreateProjectAsync("WEB");
        var keys = new List<string>();
        foreach (var summary in summaries)
        {
            var fields = summary is null ? Scenario.Fields() : Scenario.Fields(("summary", summary));
            keys.Add((await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "範例任務", Fields = fields })).Key);
        }

        return [.. keys];
    }

    private static Task<ChangeSetDetail> ProposeNumberAsync(Scenario scenario, ConversionFailurePolicy onFailure) =>
        ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.Field("summary", f => Retyped(f, FieldType.Number)), BlueprintEdits.Converts("summary", onFailure));

    private async Task<string?[]> SummaryValuesAsync(Scenario scenario, string[] keys)
    {
        var values = new List<string?>();
        foreach (var key in keys)
        {
            var item = (await _host.Items.GetAsync(scenario.Admin, key, CancellationToken.None)).Value;
            values.Add(item.Fields.TryGetValue("summary", out var value) ? ActivityLog.Text(value) : null);
        }

        return [.. values];
    }
}
