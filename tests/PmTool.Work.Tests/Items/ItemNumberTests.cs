using System.Globalization;
using System.Text.Json;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 數字類的值（估計、數字欄位、金額欄位）：絕對值最多 99,999,999,999.9999、小數最多 4 位，
/// 存進去的是正規化後的值，新增當下回的和之後讀回來的一樣。用 simple_tasks 的點數（數字）與預算（金額）。
/// </summary>
[TestFixture]
public sealed class ItemNumberTests
{
    private const string Limit = "99,999,999,999.9999";

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    public static IEnumerable<TestCaseData> OutOfRangeNumbers()
    {
        yield return Case("story_points", "1e-16384", "資料庫的 numeric 存不下");
        yield return Case("story_points", "1e-5000", "小到轉成 decimal 會變成 0");
        yield return Case("story_points", "0.00001", "小數超過 4 位");
        yield return Case("story_points", "1.000000000000000000000000000001", "位數多到轉成 decimal 會四捨五入");
        yield return Case("story_points", "100000000000", "超過上限");
        yield return Case("story_points", "1e400", "超出 double 的範圍");
        yield return Case("story_points", "1e99999999999", "指數本身就超出範圍");
        yield return Case("budget", "-100000000000", "負數看絕對值");
        yield return Case("budget", "99999999999.99995", "比上限多一點點");
    }

    [TestCaseSource(nameof(OutOfRangeNumbers))]
    public async Task CreateAsync_NumberOutOfRange_IsInvalidAndNamesTheLimits(string field, string json)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", TaskWith(field, json), CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, field);
        Assert.That(result.Failure!.Message, Does.Contain(Limit).And.Contain("小數最多 4 位"));
    }

    [TestCase("1e2", "100")]
    [TestCase("1E+2", "100")]
    [TestCase("2.50", "2.5")]
    [TestCase("12345e-4", "1.2345")]
    [TestCase("-0", "0")]
    [TestCase("0.0001", "0.0001")]
    [TestCase("99999999999.9999", "99999999999.9999")]
    [TestCase("-99999999999.9999", "-99999999999.9999")]
    public async Task CreateAsync_NumberWithinRange_ReturnsAndReadsBackTheNormalizedValue(string json, string expected)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await scenario.CreateItemAsync("WEB", TaskWith("budget", json));
        var readBack = await _host.Items.GetAsync(scenario.Admin, created.Key, CancellationToken.None);

        // Assert
        Assert.That(created.Fields["budget"].GetRawText(), Is.EqualTo(expected));
        Assert.That(readBack.Value.Fields["budget"].GetRawText(), Is.EqualTo(expected));
    }

    [Test]
    public async Task UpdateAsync_NumberField_ReturnsAndReadsBackTheNormalizedValue()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateTaskAsync("WEB");
        var patch = new WorkItemPatch { Fields = new Dictionary<string, JsonElement> { ["story_points"] = Parse("8.0e0") } };

        // Act
        var updated = await _host.Items.UpdateAsync(scenario.Admin, original.Key, patch, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, original.Key, CancellationToken.None);

        // Assert
        Assert.That(updated.Value.Fields["story_points"].GetRawText(), Is.EqualTo("8"));
        Assert.That(readBack.Value.Fields["story_points"].GetRawText(), Is.EqualTo("8"));
    }

    [Test]
    public async Task UpdateAsync_NumberOutOfRange_IsInvalidAndKeepsTheOldValue()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateItemAsync("WEB", TaskWith("story_points", "3"));
        var patch = new WorkItemPatch { Fields = new Dictionary<string, JsonElement> { ["story_points"] = Parse("1e-16384") } };

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, original.Key, patch, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, original.Key, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "story_points");
        Assert.That(readBack.Value.Fields["story_points"].GetRawText(), Is.EqualTo("3"));
    }

    public static IEnumerable<TestCaseData> OutOfRangeEstimates()
    {
        yield return new TestCaseData(100_000_000_000m).SetArgDisplayNames("超過上限");
        yield return new TestCaseData(99_999_999_999.99995m).SetArgDisplayNames("比上限多一點點");
        yield return new TestCaseData(0.00001m).SetArgDisplayNames("小數超過 4 位");
        yield return new TestCaseData(decimal.MaxValue).SetArgDisplayNames("28 位數");
    }

    [TestCaseSource(nameof(OutOfRangeEstimates))]
    public async Task CreateAsync_EstimateOutOfRange_IsInvalidAndNamesTheLimits(decimal estimate)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = new NewWorkItem { Type = "task", Title = "任務", Estimate = estimate };

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", item, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "estimate");
        Assert.That(result.Failure!.Message, Does.Contain(Limit).And.Contain("小數最多 4 位"));
    }

    [Test]
    public async Task UpdateAsync_EstimateOutOfRange_IsInvalidAndKeepsTheOldValue()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Estimate = 2m });

        // Act
        var result = await _host.Items.UpdateAsync(
            scenario.Admin, original.Key, new WorkItemPatch { Estimate = 0.12345m }, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, original.Key, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "estimate");
        Assert.That(readBack.Value.Estimate, Is.EqualTo(2m));
    }

    public static IEnumerable<TestCaseData> EstimatesWithinRange()
    {
        yield return new TestCaseData(3.50000m, "3.5").SetArgDisplayNames("尾端的 0 去掉");
        yield return new TestCaseData(99_999_999_999.9999m, "99999999999.9999").SetArgDisplayNames("剛好是上限");
        yield return new TestCaseData(0.0001m, "0.0001").SetArgDisplayNames("最小的單位");
    }

    [TestCaseSource(nameof(EstimatesWithinRange))]
    public async Task CreateAsync_EstimateWithinRange_ReturnsAndReadsBackTheNormalizedValue(decimal estimate, string expected)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Estimate = estimate });
        var readBack = await _host.Items.GetAsync(scenario.Admin, created.Key, CancellationToken.None);

        // Assert：比的是寫法，3.5 與 3.50000 用 decimal 比會相等。
        Assert.That(created.Estimate?.ToString(CultureInfo.InvariantCulture), Is.EqualTo(expected));
        Assert.That(readBack.Value.Estimate?.ToString(CultureInfo.InvariantCulture), Is.EqualTo(expected));
    }

    private static NewWorkItem TaskWith(string field, string json) =>
        new() { Type = "task", Title = "任務", Fields = new Dictionary<string, JsonElement> { [field] = Parse(json) } };

    private static TestCaseData Case(string field, string json, string because) =>
        new TestCaseData(field, json).SetArgDisplayNames(field, because);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
