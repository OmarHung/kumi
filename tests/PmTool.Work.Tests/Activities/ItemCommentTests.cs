namespace PmTool.Work.Tests.Activities;

/// <summary>留言的內容：純文字、可以換行、去掉頭尾空白後存；長度上限與不能有的字元。</summary>
[TestFixture]
public sealed class ItemCommentTests
{
    private const int MaxLength = 10_000;

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task AddCommentAsync_ReturnsTheCommentAndListsItWithLineBreaksKept()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var commenter = await scenario.MemberAsync("WEB", "commenter");
        var item = await scenario.CreateTaskAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromMinutes(3));

        // Act
        var result = await _host.Activity.AddCommentAsync(commenter, "web-1", "  \n第一行\n第二行\r\n\t縮排的第三行  \n", null, CancellationToken.None);
        var listed = await ActivityLog.LastAsync(_host, commenter, item.Key);

        // Assert
        var comment = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                (comment.ItemKey, comment.Kind, comment.ActorId, comment.At, comment.Comment),
                Is.EqualTo((item.Key, ActivityKind.Commented, commenter.PersonId, item.CreatedAt.AddMinutes(3), "第一行\n第二行\n\t縮排的第三行")));
            Assert.That(comment.Changes, Is.Empty);
            Assert.That((comment.FromState, comment.ToState), Is.EqualTo(((string?)null, (string?)null)));
            Assert.That((listed.Id, listed.ItemKey, listed.Kind, listed.ActorId, listed.At, listed.Comment), Is.EqualTo(
                (comment.Id, comment.ItemKey, comment.Kind, comment.ActorId, comment.At, comment.Comment)));
        }
    }

    [Test]
    public async Task AddCommentAsync_DoesNotChangeTheItem()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "看過了", null, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(readBack.Value, Is.EqualTo(item with { Fields = readBack.Value.Fields }));
        Assert.That(readBack.Value.UpdatedAt, Is.EqualTo(item.CreatedAt));
    }

    [Test]
    public async Task AddCommentAsync_EmojiAndOtherScripts_AreKept()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var body = "好 " + char.ConvertFromUtf32(0x1F44D) + " ok こんにちは";
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, body, null, CancellationToken.None);
        var listed = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(result.Value.Comment, Is.EqualTo(body));
        Assert.That(listed.Comment, Is.EqualTo(body));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(" \r\n\t\n ")]
    public async Task AddCommentAsync_BlankBody_IsBodyRequiredAndRecordsNothing(string? body)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, body!, null, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.CommentBodyRequired, "body");
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task AddCommentAsync_BodyAtTheLimit_IsAcceptedEvenWithSurroundingWhitespace()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var longest = new string('留', MaxLength);

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "\n  " + longest + "  \n", null, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Comment, Is.EqualTo(longest));
    }

    [Test]
    public async Task AddCommentAsync_OneCharacterOverTheLimit_IsBodyTooLong()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, new string('留', MaxLength + 1), null, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.CommentBodyTooLong, "body");
        Assert.That(result.Failure!.Message, Does.Contain("10000").And.Contain("10001"));
    }

    [TestCase(0x0000)]
    [TestCase(0x0007)]
    [TestCase(0x000B)]
    [TestCase(0x001B)]
    [TestCase(0x007F)]
    [TestCase(0x0085)]
    [TestCase(0xD800)]
    [TestCase(0xDC00)]
    public async Task AddCommentAsync_ControlOrLoneSurrogateCharacter_IsBodyInvalidAndRecordsNothing(int character)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "前面" + (char)character + "後面", null, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.CommentBodyInvalid, "body");
        Assert.That(result.Failure!.Message, Does.Contain("換行"));
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }
}
