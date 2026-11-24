using System.Text.Json;

namespace PmTool.Work.Tests.Items;

/// <summary>自訂欄位的值：每種型別的合法形狀、正規化，以及擋下來的情況。用 simple_tasks，它每種型別各有一個欄位。</summary>
[TestFixture]
public sealed class ItemFieldTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    public static IEnumerable<TestCaseData> InvalidValues()
    {
        yield return Case("summary", "12", "文字欄位不能填數字");
        yield return Case("summary", Quoted(new string('a', 2_001)), "文字超過上限");
        yield return Case("summary", "\"第一行\\n第二行\"", "單行文字不能換行");
        yield return Case("notes", Quoted(new string('a', 50_001)), "長文字超過上限");
        yield return Case("notes", "\"壞掉\\u0000的\"", "長文字不能有 NUL");
        yield return Case("story_points", "\"3\"", "數字不能用字串");
        yield return Case("story_points", "true", "數字不能是布林");
        yield return Case("budget", "\"100\"", "金額不能用字串");
        yield return Case("review_date", "\"2026-13-01\"", "沒有十三月");
        yield return Case("review_date", "\"2026/11/01\"", "日期要用連字號");
        yield return Case("review_date", "\"2026-11-1\"", "日要兩位數");
        yield return Case("review_date", "20261101", "日期不能是數字");
        yield return Case("category", "\"epic\"", "不是定義過的選項");
        yield return Case("category", "[\"bug\"]", "單選不能是陣列");
        yield return Case("tags", "\"frontend\"", "多選要是陣列");
        yield return Case("tags", "[\"frontend\",\"nope\"]", "陣列裡有沒定義的選項");
        yield return Case("tags", "[\"frontend\",\"frontend\"]", "選項重複");
        yield return Case("tags", "[1]", "選項要是字串");
        yield return Case("reviewer", "\"not-a-guid\"", "成員要是人員編號");
        yield return Case("reviewer", "5", "成員不能是數字");
        yield return Case("blocked", "\"yes\"", "勾選要是布林");
        yield return Case("reference_url", "\"ftp://example.com/file\"", "只接受 http 與 https");
        yield return Case("reference_url", "\"example.com\"", "要是絕對網址");
        yield return Case("reference_url", "\"/relative/path\"", "Unix 上的絕對路徑不是網址");
        yield return Case("reference_url", "\"https://exa mple.com\"", "網址不能有空白");
        yield return Case("reference_url", "{\"href\":\"https://example.com\"}", "網址要是字串");
    }

    [TestCaseSource(nameof(InvalidValues))]
    public async Task CreateAsync_ValueThatDoesNotFitTheFieldType_IsInvalid(string field, string json)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = new NewWorkItem { Type = "task", Title = "任務", Fields = new Dictionary<string, JsonElement> { [field] = Parse(json) } };

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", item, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, field);
    }

    [Test]
    public async Task CreateAsync_EveryFieldTypeWithAValidValue_ReadsBackTheSameValues()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var fields = Scenario.Fields(
            ("category", "bug"),
            ("tags", new[] { "backend", "frontend" }),
            ("review_date", "2026-11-30"),
            ("story_points", 2.5),
            ("budget", 120000),
            ("blocked", true),
            ("reference_url", "https://example.com/spec?id=1"),
            ("reviewer", scenario.Admin.PersonId.ToString().ToUpperInvariant()),
            ("summary", "一句話說明"),
            ("notes", "第一行\n第二行\t縮排"));

        // Act
        var created = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "全部欄位", Fields = fields });
        var readBack = await _host.Items.GetAsync(scenario.Admin, created.Key, CancellationToken.None);

        // Assert：成員欄位存成標準格式的人員編號，其餘照原樣。
        var values = readBack.Value.Fields;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(values["category"].GetString(), Is.EqualTo("bug"));
            Assert.That(values["tags"].EnumerateArray().Select(t => t.GetString()), Is.EqualTo(new[] { "backend", "frontend" }));
            Assert.That(values["review_date"].GetString(), Is.EqualTo("2026-11-30"));
            Assert.That(values["story_points"].GetDecimal(), Is.EqualTo(2.5m));
            Assert.That(values["budget"].GetDecimal(), Is.EqualTo(120000m));
            Assert.That(values["blocked"].GetBoolean(), Is.True);
            Assert.That(values["reference_url"].GetString(), Is.EqualTo("https://example.com/spec?id=1"));
            Assert.That(values["reviewer"].GetString(), Is.EqualTo(scenario.Admin.PersonId.ToString()));
            Assert.That(values["summary"].GetString(), Is.EqualTo("一句話說明"));
            Assert.That(values["notes"].GetString(), Is.EqualTo("第一行\n第二行\t縮排"));
            Assert.That(created.Fields.Keys, Is.EquivalentTo(values.Keys));
        }
    }

    [Test]
    public async Task CreateAsync_EmptyValues_AreNotStored()
    {
        // Arrange：空字串、空陣列、沒勾、null 都表示沒有值。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var fields = Scenario.Fields(("summary", "  "), ("reference_url", ""), ("tags", Array.Empty<string>()), ("blocked", false), ("category", null));

        // Act
        var created = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "空值", Fields = fields });
        var readBack = await _host.Items.GetAsync(scenario.Admin, created.Key, CancellationToken.None);

        // Assert
        Assert.That(readBack.Value.Fields, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_FieldNotDefinedInTheBlueprint_IsUnknownField()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "任務", Fields = Scenario.Fields(("color", "red")) }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemUnknownField, "color");
    }

    [Test]
    public async Task CreateAsync_FieldThatBelongsToAnotherType_IsUnknownField()
    {
        // Arrange：change_reason 只屬於需求變更單，需求沒有這個欄位。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin,
            "WEB",
            new NewWorkItem { Type = "requirement", Title = "需求", Fields = Scenario.Fields(("change_reason", "客戶要求")) },
            CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemUnknownField, "change_reason");
    }

    [Test]
    public async Task CreateAsync_PersonFieldWithSomeoneOutsideTheProject_IsUnknownAssignee()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var fields = Scenario.Fields(("reviewer", scenario.Person().PersonId.ToString()));

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "任務", Fields = fields }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemUnknownAssignee, "reviewer");
        Assert.That(result.Failure!.Message, Does.Contain("審查者"));
    }

    [Test]
    public async Task CreateAsync_InvalidValue_ExplainsWithTheFieldsDisplayName()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var fields = Scenario.Fields(("review_date", "下週一"));

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "任務", Fields = fields }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Message, Does.Contain("「檢查日」").And.Contain("YYYY-MM-DD"));
    }

    private static TestCaseData Case(string field, string json, string because) =>
        new TestCaseData(field, json).SetArgDisplayNames(field, because);

    private static string Quoted(string text) => JsonSerializer.Serialize(text);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
