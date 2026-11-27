using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>收件匣的分類與搜尋：分類、比對的欄位、跳脫、做事的人的名稱、組合、驗證、看不到的專案。</summary>
[TestFixture]
public sealed class InboxFilterTests : ConsumerTestBase
{
    private static Actor As(EventBuilder item, Guid person) => new(item.OrganizationId, person, false);

    private async Task<NotificationPage> ListAsync(EventBuilder item, Guid me, NotificationQuery query) =>
        (await Host.Inbox.ListAsync(As(item, me), query, CancellationToken.None)).Value;

    private static NotificationQuery Query(
        NotificationCategory category = NotificationCategory.All, string? search = null, bool unread = false, string? cursor = null, int limit = 50) =>
        new(unread, cursor, limit) { Category = category, Search = search };

    /// <summary>me 依序收到：指派、提及、回覆、關注的留言、改狀態。</summary>
    private async Task<(EventBuilder Item, Guid Me, Guid Other)> FiveKindsAsync()
    {
        var (item, me, other, _, _) = NewItem();
        await HandleAsync(item.Created(me));
        await HandleAsync(item.Updated(other, me, true));
        await HandleAsync(item.Commented(other, "請看一下", me));
        await HandleAsync(item.Replied(other, me, "回覆你"));
        await HandleAsync(item.Commented(other, "一般留言"));
        await HandleAsync(item.Transitioned(other, "待辦", "審查中"));
        return (item, me, other);
    }

    [Test]
    public async Task List_CategoryMentions_ReturnsOnlyMentionedAndReplied()
    {
        // Arrange
        var (item, me, _) = await FiveKindsAsync();

        // Act
        var page = await ListAsync(item, me, Query(NotificationCategory.Mentions));

        // Assert
        Assert.That(page.Items.Select(n => n.Kind).Distinct(),
            Is.EquivalentTo(new[] { NotificationKind.Mentioned, NotificationKind.Replied }));
    }

    [Test]
    public async Task List_CategoryAssigned_ReturnsOnlyAssigned()
    {
        // Arrange
        var (item, me, _) = await FiveKindsAsync();

        // Act
        var page = await ListAsync(item, me, Query(NotificationCategory.Assigned));

        // Assert
        Assert.That(page.Items.Select(n => n.Kind), Is.EqualTo(new[] { NotificationKind.Assigned }));
    }

    [Test]
    public async Task List_CategoryAll_ReturnsEveryKind()
    {
        // Arrange
        var (item, me, _) = await FiveKindsAsync();

        // Act
        var all = await ListAsync(item, me, Query(NotificationCategory.All));
        var mentions = await ListAsync(item, me, Query(NotificationCategory.Mentions));
        var assigned = await ListAsync(item, me, Query(NotificationCategory.Assigned));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(all.Items.Select(n => n.Kind).Distinct(), Does.Contain(NotificationKind.Commented).And.Contain(NotificationKind.StateChanged));
            Assert.That(all.Items, Has.Count.GreaterThan(mentions.Items.Count + assigned.Items.Count));
        }
    }

    [TestCase("WEB-1")]
    [TestCase("範例項目")]
    [TestCase("web")]
    [TestCase("請看一下")]
    [TestCase("審查中")]
    [TestCase("待辦")]
    [TestCase("  審查中  ")]
    public async Task List_SearchByField_FindsTheNotification(string search)
    {
        // Arrange
        var (item, me, _) = await FiveKindsAsync();

        // Act
        var page = await ListAsync(item, me, Query(search: search));

        // Assert
        Assert.That(page.Items, Is.Not.Empty);
    }

    [Test]
    public async Task List_SearchByText_IsCaseInsensitiveAndNarrowsTheList()
    {
        // Arrange
        var (item, me, _) = await FiveKindsAsync();

        // Act
        var page = await ListAsync(item, me, Query(search: "回覆你"));
        var upper = await ListAsync(item, me, Query(search: "web-1"));
        var none = await ListAsync(item, me, Query(search: "不存在的字"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(n => n.Kind), Is.EqualTo(new[] { NotificationKind.Replied }));
            Assert.That(upper.Items, Is.Not.Empty);
            Assert.That(none.Items, Is.Empty);
        }
    }

    [TestCase("%")]
    [TestCase("_")]
    [TestCase("\\")]
    public async Task List_SearchWithLikeWildcards_MatchesLiterally(string search)
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await HandleAsync(item.Created(me));
        await HandleAsync(item.Commented(other, "進度 100% 完成"));
        await HandleAsync(item.Commented(other, "一般的留言"));
        await HandleAsync(item.Commented(other, "snake_case 名稱"));
        await HandleAsync(item.Commented(other, "路徑 a\\b"));

        // Act
        var page = await ListAsync(item, me, Query(search: search));

        // Assert
        var excerpt = search switch { "%" => "進度 100% 完成", "_" => "snake_case 名稱", _ => "路徑 a\\b" };
        Assert.That(page.Items.Select(n => n.Excerpt), Is.EqualTo(new[] { excerpt }));
    }

    [Test]
    public async Task List_SearchByActorDisplayName_FindsWhatThatPersonDid()
    {
        // Arrange
        var (item, me, other, third, _) = NewItem();
        Host.People.Add(item.OrganizationId, other, "王小明", null);
        Host.People.Add(item.OrganizationId, third, "林小華", null);
        await HandleAsync(item.Created(me));
        await HandleAsync(item.Commented(other, "甲的留言"));
        await HandleAsync(item.Commented(third, "乙的留言"));

        // Act
        var page = await ListAsync(item, me, Query(search: "小明"));

        // Assert
        Assert.That(page.Items.Select(n => n.Excerpt), Is.EqualTo(new[] { "甲的留言" }));
    }

    [Test]
    public async Task List_CategoryWithSearchUnreadAndCursor_WorkTogether()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        Host.People.Add(item.OrganizationId, other, "王小明", null);
        await HandleAsync(item.Created(me));
        for (var i = 0; i < 4; i++)
        {
            await HandleAsync(item.Commented(other, $"提及 {i}", me));
        }

        await HandleAsync(item.Commented(other, "沒提及"));
        var first = await ListAsync(item, me, Query(NotificationCategory.Mentions, "小明", unread: true, limit: 3));
        await Host.Inbox.MarkReadAsync(As(item, me), [first.Items[0].NotificationId], CancellationToken.None);

        // Act
        var again = await ListAsync(item, me, Query(NotificationCategory.Mentions, "小明", unread: true, limit: 2));
        var next = await ListAsync(item, me, Query(NotificationCategory.Mentions, "小明", unread: true, cursor: again.NextCursor, limit: 2));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Items, Has.Count.EqualTo(3));
            Assert.That(first.NextCursor, Is.Not.Null);
            Assert.That(again.Items.Select(n => n.Excerpt), Is.EqualTo(new[] { "提及 2", "提及 1" }));
            Assert.That(next.Items.Select(n => n.Excerpt), Is.EqualTo(new[] { "提及 0" }));
            Assert.That(next.NextCursor, Is.Null);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task List_BlankSearch_IsTheSameAsNoSearch(string? search)
    {
        // Arrange
        var (item, me, _) = await FiveKindsAsync();

        // Act
        var plain = await ListAsync(item, me, Query());
        var blank = await ListAsync(item, me, Query(search: search));

        // Assert
        Assert.That(blank.Items.Select(n => n.NotificationId), Is.EqualTo(plain.Items.Select(n => n.NotificationId)));
    }

    [Test]
    public async Task List_SearchTooLongOrWithControlCharactersOrLoneSurrogates_FailsOnQ()
    {
        // Arrange
        var (item, me, _) = await FiveKindsAsync();
        string[] bad = [new string('a', NotificationRules.MaxSearchLength + 1), "a\u0000b", "a\nb", "a\tb", "a\uD800b", "a\uDC00"];

        foreach (var search in bad)
        {
            // Act
            var result = await Host.Inbox.ListAsync(As(item, me), Query(search: search), CancellationToken.None);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Failure?.Code, Is.EqualTo(NotificationErrorCodes.QueryInvalid));
                Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "q" }));
            }
        }
    }

    [Test]
    public async Task List_SearchWithEmoji_IsAccepted()
    {
        // Arrange
        var (item, me, _) = await FiveKindsAsync();

        // Act
        var result = await Host.Inbox.ListAsync(As(item, me), Query(search: "修正 \U0001F44C"), CancellationToken.None);

        // Assert
        Assert.That(result.Failure, Is.Null);
    }

    [Test]
    public async Task List_SearchOfExactlyTheMaximumLengthAfterTrim_IsAccepted()
    {
        // Arrange
        var (item, me, _) = await FiveKindsAsync();
        var search = " " + new string('a', NotificationRules.MaxSearchLength) + " ";

        // Act
        var result = await Host.Inbox.ListAsync(As(item, me), Query(search: search), CancellationToken.None);

        // Assert
        Assert.That(result.Failure, Is.Null);
    }

    [Test]
    public async Task List_SearchMatchesNotificationsOfAProjectNoLongerReadable_StillHidden()
    {
        // Arrange
        var (item, me, other, _, _) = NewItem();
        await HandleAsync(item.Created(me));
        await HandleAsync(item.Commented(other, "機密的字樣"));
        Host.Access.Revoke(item.ProjectId, me);

        // Act
        var page = await ListAsync(item, me, Query(search: "機密"));

        // Assert
        Assert.That(page.Items, Is.Empty);
    }
}
