namespace PmTool.Work.Tests.Events;

/// <summary>留言摘要的切法：提及換成「@名稱」、不超過上限、不從代理字元對的中間切開。</summary>
[TestFixture]
public sealed class MentionTextTests
{
    [Test]
    public async Task AddCommentAsync_ExcerptEndingInTheMiddleOfAnEmoji_DoesNotSplitTheSurrogatePair()
    {
        // Arrange：第 200 個位置落在表情符號（兩個 UTF-16 單元）的中間。
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var body = new string('字', WorkEventRules.MaxExcerptLength - 1) + "😀後面還有";

        // Act
        await host.Activity.AddCommentAsync(scenario.Admin, item.Key, body, null, CancellationToken.None);
        var excerpt = (await EventFeed.LastAsync(host, scenario)).Item.CommentExcerpt;

        // Assert
        Assert.That(excerpt, Is.EqualTo(new string('字', WorkEventRules.MaxExcerptLength - 1)));
    }

    [Test]
    public async Task AddCommentAsync_MentionLongerThanItsNameInTheText_IsMeasuredAfterTheReplacement()
    {
        // Arrange：連結很長，換成「@名稱」之後才在上限內，摘要不該被連結的長度吃掉。
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        var member = await scenario.MemberAsync("WEB", "viewer");
        var item = await scenario.CreateTaskAsync("WEB");
        var body = string.Concat(Enumerable.Repeat($"{EventFeed.Mention(member, "名")} ", 4)) + "結尾";

        // Act
        await host.Activity.AddCommentAsync(scenario.Admin, item.Key, body, null, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(host, scenario)).Item.CommentExcerpt, Is.EqualTo("@名 @名 @名 @名 結尾"));
    }

    [Test]
    public async Task AddCommentAsync_ImagesAndFileLinks_BecomeAPlaceholderAndTheFileName()
    {
        // Arrange
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var body = $"截圖 ![畫面.png](attachment:{Guid.NewGuid():D}) 與 [報價\\]單.xlsx](attachment:{Guid.NewGuid():D})";

        // Act
        await host.Activity.AddCommentAsync(scenario.Admin, item.Key, body, null, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(host, scenario)).Item.CommentExcerpt, Is.EqualTo("截圖 ［圖片］ 與 報價]單.xlsx"));
    }
}
