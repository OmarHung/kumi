using System.Text.Json;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 換行的寫法：\r\n 與單獨的 \r 一律存成 \n。網頁的文字框送出的是 \n，其他用戶端可能送 \r\n；
/// 寫法不同但內容相同時不能當成有改，長度也要用正規化後的算。
/// </summary>
[TestFixture]
public sealed class LineBreakTests
{
    private const string Mixed = "第一行\r\n第二行\r第三行";
    private const string Normalized = "第一行\n第二行\n第三行";

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_DescriptionWithCrLfAndLoneCr_StoresLineFeeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = Mixed });
        var readBack = await _host.Items.GetAsync(scenario.Admin, created.Key, CancellationToken.None);

        // Assert
        Assert.That(created.Description, Is.EqualTo(Normalized));
        Assert.That(readBack.Value.Description, Is.EqualTo(Normalized));
    }

    [Test]
    public async Task UpdateAsync_SameDescriptionWithLineFeedsInsteadOfCrLf_IsNotAChange()
    {
        // Arrange：用 \r\n 存進去，網頁的文字框原樣送回來時是 \n。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "第一行\r\n第二行" });
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var updated = await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { Description = "第一行\n第二行" }, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That((updated.Value.Description, updated.Value.UpdatedAt), Is.EqualTo(("第一行\n第二行", item.UpdatedAt)));
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task CreateAsync_DescriptionThatFitsWhenCrLfCountsAsOneCharacter_IsAccepted()
    {
        // Arrange：25,000 個字，中間 24,999 個 \r\n；原文 74,998 個字元，正規化後 49,999 個字。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var description = string.Join("\r\n", Enumerable.Repeat("說", 25_000));

        // Act
        var created = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = description });

        // Assert
        Assert.That(created.Description, Has.Length.EqualTo(49_999));
    }

    [Test]
    public async Task AddCommentAsync_CrLfAndLoneCr_AreStoredAsLineFeeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // 同一個時刻的活動依編號排，編號在同一毫秒內是隨機的；不推進時鐘的話，「最後一筆」不一定是這則留言。
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, Mixed, null, CancellationToken.None);
        var listed = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(result.Value.Comment, Is.EqualTo(Normalized));
        Assert.That(listed.Comment, Is.EqualTo(Normalized));
    }

    [Test]
    public async Task AddCommentAsync_BodyThatFitsWhenCrLfCountsAsOneCharacter_IsAccepted()
    {
        // Arrange：原文 14,998 個字元，正規化後 9,999 個字，在 10,000 字的上限以內。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var body = string.Join("\r\n", Enumerable.Repeat("留", 5_000));

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, body, null, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Comment, Has.Length.EqualTo(9_999));
    }

    [Test]
    public async Task CreateAsync_LongTextWithCrLfAndLoneCr_StoresLineFeeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await scenario.CreateItemAsync("WEB", Task(("notes", Mixed)));
        var readBack = await _host.Items.GetAsync(scenario.Admin, created.Key, CancellationToken.None);

        // Assert
        Assert.That(created.Fields["notes"].GetString(), Is.EqualTo(Normalized));
        Assert.That(readBack.Value.Fields["notes"].GetString(), Is.EqualTo(Normalized));
    }

    [Test]
    public async Task UpdateAsync_SameLongTextWithLineFeedsInsteadOfCrLf_IsNotAChange()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", Task(("notes", "第一行\r\n第二行")));
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var updated = await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { Fields = Scenario.Fields(("notes", "第一行\n第二行")) }, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(updated.Value.UpdatedAt, Is.EqualTo(item.UpdatedAt));
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task CreateAsync_LongTextThatFitsWhenCrLfCountsAsOneCharacter_IsAccepted()
    {
        // Arrange：原文 74,998 個字元，正規化後 49,999 個字，在 50,000 字的上限以內。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var notes = string.Join("\r\n", Enumerable.Repeat("備", 25_000));

        // Act
        var created = await scenario.CreateItemAsync("WEB", Task(("notes", notes)));

        // Assert
        Assert.That(created.Fields["notes"].GetString(), Has.Length.EqualTo(49_999));
    }

    [Test]
    public async Task CreateAsync_SingleLineTextEndingInCrLf_IsRejectedForTheLineBreakNotTheLength()
    {
        // Arrange：1,999 個字加 \r\n，正規化後剛好 2,000 個字，不超過上限；擋下它的是換行。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var summary = new string('摘', 1_999) + "\r\n";

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", Task(("summary", summary)), CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "summary");
        Assert.That(result.Failure!.Message, Is.EqualTo("「摘要」不能有換行、控制字元或無效的字元。"));
    }

    private static NewWorkItem Task(params (string Key, object? Value)[] fields) =>
        new() { Type = "task", Title = "任務", Fields = Scenario.Fields(fields) };
}
