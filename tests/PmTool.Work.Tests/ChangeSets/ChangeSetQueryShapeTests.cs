using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>
/// 變更集的查詢讀了什麼、什麼時候鎖住專案。結果本身在別的測試裡確認，這裡看送到資料庫的 SQL：
/// 清單不讀變更集全文、預覽不讀整份自訂欄位、套用不讀項目的說明，送出時算完影響才發流水號。
/// </summary>
[TestFixture]
public sealed partial class ChangeSetQueryShapeTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_DoesNotReadTheChangeSetDocuments()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Views = [] });
        using var sql = new SqlLog();
        await using var provider = ProviderWith(sql);
        await using var scope = provider.CreateAsyncScope();
        var changes = scope.ServiceProvider.GetRequiredService<IBlueprintChangeService>();
        await changes.GetAsync(scenario.Admin, "WEB", 1, CancellationToken.None);
        var reading = sql.Commands;
        sql.Clear();

        // Act
        var listed = await changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert：讀單一份時有讀全文，證明真的收集得到。
        var listing = sql.Commands.Where(c => c.Contains("work.changesets", StringComparison.Ordinal)).ToArray();
        Assert.That(listed.Value.Select(c => (c.Number, c.Summary)), Is.EqualTo(new[] { (1L, (string?)ChangeSetDrafts.Summary) }));
        Assert.That(reading.Where(c => c.Contains("work.changesets", StringComparison.Ordinal)), Has.Some.Contains("document"));
        Assert.That(listing, Is.Not.Empty);
        Assert.That(listing, Has.None.Contains("document"));
    }

    [Test]
    public async Task ProposeAsync_IssuesTheNumberOnlyAfterTheImpactsAreCounted()
    {
        // Arrange：轉換失敗就擋下，算影響時要逐一看有值的項目。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await SummaryTaskAsync(scenario, "不是數字");
        var document = await ChangeSetDrafts.DraftAsync(
            scenario, "WEB", b => b.Field("summary", f => f with { Type = FieldType.Number }), BlueprintEdits.Converts("summary", ConversionFailurePolicy.Abort));
        using var sql = new SqlLog();
        await using var provider = ProviderWith(sql);
        await using var scope = provider.CreateAsyncScope();
        var changes = scope.ServiceProvider.GetRequiredService<IBlueprintChangeService>();

        // Act
        var proposed = await changes.ProposeAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None);

        // Assert：發號的 UPDATE 會鎖住專案那一列到提交，同一個專案的新增項目都要等它；所以它排在讀項目的查詢之後。
        var commands = sql.Commands.ToList();
        var numbering = commands.FindIndex(c => c.Contains("UPDATE work.projects", StringComparison.Ordinal));
        var lastItemQuery = commands.FindLastIndex(c => c.Contains("work.items", StringComparison.Ordinal));
        Assert.That(proposed.Value.Blockers.Select(b => b.Code), Is.EqualTo(new[] { WorkErrorCodes.ChangeSetConversionFailed }));
        Assert.That(lastItemQuery, Is.GreaterThan(0));
        Assert.That(numbering, Is.GreaterThan(lastItemQuery));
    }

    [Test]
    public async Task GetAsync_ConversionThatAbortsOnFailure_ReadsOnlyThatFieldsValues()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await SummaryTaskAsync(scenario, "42");
        await SummaryTaskAsync(scenario, "不是數字");
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.Field("summary", f => f with { Type = FieldType.Number }), BlueprintEdits.Converts("summary", ConversionFailurePolicy.Abort));
        using var sql = new SqlLog();
        await using var provider = ProviderWith(sql);
        await using var scope = provider.CreateAsyncScope();
        var changes = scope.ServiceProvider.GetRequiredService<IBlueprintChangeService>();

        // Act
        var detail = await changes.GetAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert：選出來的欄位裡，fields 只以「fields -> 某個 key」的形式出現。
        Assert.That(detail.Value.Blockers.Single().Message, Does.Contain("WEB-2"));
        Assert.That(SelectLists(sql.Commands), Has.Some.Contains("fields ->"));
        Assert.That(SelectLists(sql.Commands), Has.None.Match(WholeFields().ToString()));
    }

    [Test]
    public async Task ApplyAsync_DoesNotReadWholeItemRows()
    {
        // Arrange：項目的說明可能很長，套用用不到它。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "有摘要",
            Description = "很長的說明",
            Fields = Scenario.Fields(("summary", "範例摘要")),
        });
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b.WithoutField("summary"), BlueprintEdits.Drops("summary"));
        using var sql = new SqlLog();
        await using var provider = ProviderWith(sql);
        await using var scope = provider.CreateAsyncScope();
        var changes = scope.ServiceProvider.GetRequiredService<IBlueprintChangeService>();

        // Act
        var applied = await changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        var itemQueries = sql.Commands.Where(c => c.Contains("work.items", StringComparison.Ordinal)).ToArray();
        Assert.That(applied.Succeeded, Is.True, () => ChangeSetDrafts.Describe(applied.Failure!));
        Assert.That(itemQueries, Has.Some.Contains("FOR NO KEY UPDATE"));
        Assert.That(itemQueries, Has.None.Contains("description"));
        Assert.That(itemQueries, Has.None.Contains("SELECT *"));
    }

    /// <summary>每個查詢 SELECT 與 FROM 之間的那一段，也就是讀了哪些欄位。</summary>
    private static string[] SelectLists(IEnumerable<string> commands) =>
        [.. commands.SelectMany(c => SelectList().Matches(c).Select(m => m.Groups["columns"].Value))];

    [GeneratedRegex(@"SELECT\s(?<columns>.*?)\sFROM\s", RegexOptions.Singleline)]
    private static partial Regex SelectList();

    /// <summary>整欄的 fields：後面不是 -> 的。</summary>
    [GeneratedRegex(@"\bfields\b(?!\s*->)")]
    private static partial Regex WholeFields();

    private ServiceProvider ProviderWith(SqlLog sql) =>
        WorkTestHost.BuildProvider(_host.ConnectionString, _host.Clock, services => services.AddLogging(logging => logging.AddProvider(sql)));

    private static Task<WorkItem> SummaryTaskAsync(Scenario scenario, string summary) =>
        scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "有摘要", Fields = Scenario.Fields(("summary", summary)) });
}
