using PmTool.Notifications.Email;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Tests;

/// <summary>信的內容：主旨、連結、跳脫、時間。純函式，不需要資料庫。</summary>
[TestFixture]
public sealed class EmailComposerTests
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Actor = Guid.CreateVersion7();
    private static readonly DateTimeOffset At = new(2026, 11, 21, 6, 5, 0, TimeSpan.Zero);

    private static EmailComposer.Context Context(params (Guid Id, string Name)[] names) => new(
        "team@example.com",
        "Example 公司",
        "me@example.com",
        "https://pm.example.com",
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei"),
        names.Length == 0 ? new Dictionary<Guid, string> { [Actor] = "王小明" } : names.ToDictionary(n => n.Id, n => n.Name));

    private static NotificationRow Row(
        NotificationKind kind, string itemKey = "MNT-12", string title = "修正登入頁", string? excerpt = null, string project = "MNT", Guid? commentId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = Guid.CreateVersion7(),
        RecipientId = Me,
        Kind = kind,
        ProjectId = Guid.CreateVersion7(),
        ProjectKey = project,
        ItemId = Guid.NewGuid(),
        ItemKey = itemKey,
        ItemTitle = title,
        ActorPersonId = Actor,
        FromStateName = kind == NotificationKind.StateChanged ? "進行中" : null,
        ToStateName = kind == NotificationKind.StateChanged ? "完成" : null,
        Excerpt = excerpt,
        CommentId = commentId,
        EventId = Guid.CreateVersion7(),
        CreatedAt = At,
    };

    [Test]
    public void Notification_EditedExcerpt_AddsTheEditedMarkInHtmlAndText()
    {
        // Arrange
        var row = Row(NotificationKind.Commented, excerpt: "新內容") with { ExcerptEditedAt = At.AddMinutes(1) };

        // Act
        var mail = EmailComposer.Notification(Context(), [row]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.TextBody, Does.Contain("『新內容』（已編輯）"));
            Assert.That(mail.HtmlBody, Does.Contain("『新內容』（已編輯）"));
        }
    }

    [Test]
    public void Notification_ExcerptNeverEdited_HasNoEditedMark()
    {
        // Act
        var mail = EmailComposer.Notification(Context(), [Row(NotificationKind.Commented, excerpt: "原文")]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.TextBody, Does.Not.Contain("已編輯"));
            Assert.That(mail.HtmlBody, Does.Not.Contain("已編輯"));
        }
    }

    [TestCase(NotificationKind.Assigned, "王小明 指派給你：MNT-12 修正登入頁")]
    [TestCase(NotificationKind.Mentioned, "王小明 提及了你：MNT-12 修正登入頁")]
    [TestCase(NotificationKind.Commented, "王小明 留言：MNT-12 修正登入頁")]
    [TestCase(NotificationKind.StateChanged, "王小明 改了狀態：MNT-12 修正登入頁")]
    [TestCase(NotificationKind.Replied, "王小明 回覆了你的留言：MNT-12 修正登入頁")]
    public void Notification_SingleNotification_SubjectNamesTheActorAndTheAction(NotificationKind kind, string expected)
    {
        // Act
        var mail = EmailComposer.Notification(Context(), [Row(kind)]);

        // Assert
        Assert.That(mail.Subject, Is.EqualTo(expected));
    }

    [Test]
    public void Notification_SeveralNotifications_SubjectCountsThemAndNamesTheFirstItem()
    {
        // Act
        var mail = EmailComposer.Notification(Context(), [Row(NotificationKind.Assigned), Row(NotificationKind.Commented, "MNT-13", "別的")]);

        // Assert
        Assert.That(mail.Subject, Is.EqualTo("2 則新通知：MNT-12 修正登入頁 等"));
    }

    [Test]
    public void Notification_SeveralNotificationsOfTheSameItem_SubjectHasNoEtc()
    {
        // Arrange
        var first = Row(NotificationKind.Assigned);

        // Act
        var mail = EmailComposer.Notification(Context(), [first, first with { Id = Guid.CreateVersion7(), Kind = NotificationKind.Commented }]);

        // Assert
        Assert.That(mail.Subject, Is.EqualTo("2 則新通知：MNT-12 修正登入頁"));
    }

    [Test]
    public void Notification_VeryLongTitle_SubjectIsTruncatedWithAnEllipsis()
    {
        // Act
        var mail = EmailComposer.Notification(Context(), [Row(NotificationKind.Assigned, title: new string('長', 400))]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject, Has.Length.EqualTo(150));
            Assert.That(mail.Subject, Does.EndWith("…"));
        }
    }

    [Test]
    public void Notification_TitleWithLineBreaks_SubjectStaysOnOneLine()
    {
        // Act
        var mail = EmailComposer.Notification(Context(), [Row(NotificationKind.Assigned, title: "第一行\r\nBcc: attacker@example.com")]);

        // Assert
        Assert.That(mail.Subject, Does.Not.Contain('\r').And.Not.Contain('\n'));
    }

    [Test]
    public void Notification_UnknownActor_UsesThePlaceholderName()
    {
        // Act
        var mail = EmailComposer.Notification(Context((Guid.CreateVersion7(), "別人")), [Row(NotificationKind.Assigned)]);

        // Assert
        Assert.That(mail.Subject, Does.StartWith("（已不在組織） 指派給你"));
    }

    [Test]
    public void Notification_Lines_DescribeEachKindWithTheTimeInTheOrganizationZone()
    {
        // Act
        var mail = EmailComposer.Notification(Context(), [
            Row(NotificationKind.Assigned),
            Row(NotificationKind.Mentioned, excerpt: "請看一下"),
            Row(NotificationKind.Commented, excerpt: "已修好"),
            Row(NotificationKind.StateChanged),
            Row(NotificationKind.Replied, excerpt: "收到")]);

        // Assert：UTC 06:05 是台北 14:05。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.TextBody, Does.Contain("王小明 把這個項目指派給你（11/21 14:05）"));
            Assert.That(mail.TextBody, Does.Contain("王小明 提及了你：『請看一下』"));
            Assert.That(mail.TextBody, Does.Contain("王小明 留言：『已修好』"));
            Assert.That(mail.TextBody, Does.Contain("王小明 把狀態從「進行中」改成「完成」"));
            Assert.That(mail.TextBody, Does.Contain("王小明 回覆了你的留言：『收到』"));
        }
    }

    [Test]
    public void Notification_Links_PointToTheItemTheCommentTheInboxAndTheAccountPage()
    {
        // Arrange
        var commentId = Guid.CreateVersion7();

        // Act
        var mail = EmailComposer.Notification(Context(), [Row(NotificationKind.Commented, excerpt: "好", commentId: commentId)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.TextBody, Does.Contain("https://pm.example.com/projects/MNT/items/MNT-12"));
            Assert.That(mail.HtmlBody, Does.Contain($"https://pm.example.com/projects/MNT/items/MNT-12#comment-{commentId}"));
            Assert.That(mail.TextBody, Does.Contain("到收件匣看全部：https://pm.example.com/inbox"));
            Assert.That(mail.TextBody, Does.Contain("https://pm.example.com/account"));
        }
    }

    [Test]
    public void Notification_UserInput_IsEscapedInTheHtmlAndShownVerbatimInTheText()
    {
        // Arrange
        const string evil = "<script>alert(1)</script> & \"quote\"";

        // Act
        var mail = EmailComposer.Notification(Context(), [Row(NotificationKind.Commented, title: evil, excerpt: evil)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.HtmlBody, Does.Not.Contain("<script>"));
            Assert.That(mail.HtmlBody, Does.Contain("&lt;script&gt;alert(1)&lt;/script&gt;"));
            Assert.That(mail.HtmlBody, Does.Contain("&amp;"));
            Assert.That(mail.TextBody, Does.Contain(evil));
        }
    }

    [Test]
    public void Notification_ActorNameWithMarkup_IsEscaped()
    {
        // Act
        var mail = EmailComposer.Notification(Context((Actor, "<b>Eve</b>")), [Row(NotificationKind.Assigned)]);

        // Assert
        Assert.That(mail.HtmlBody, Does.Not.Contain("<b>Eve</b>").And.Contain("&lt;b&gt;Eve&lt;/b&gt;"));
    }

    [Test]
    public void Notification_GroupsLinesByItem()
    {
        // Act
        var first = Row(NotificationKind.Assigned);
        var mail = EmailComposer.Notification(Context(), [first, Row(NotificationKind.Commented, "MNT-13", "別的", "好"), first with { Id = Guid.CreateVersion7(), Kind = NotificationKind.StateChanged }]);

        // Assert：同一個項目的兩則在同一組，標題只出現一次。
        var occurrences = System.Text.RegularExpressions.Regex.Matches(mail.TextBody, "MNT-12 修正登入頁").Count;
        Assert.That(occurrences, Is.EqualTo(1));
    }

    [Test]
    public void Notification_MoreThanTheLimit_SaysHowManyAreLeft()
    {
        // Act
        var rows = Enumerable.Range(0, 23).Select(i => Row(NotificationKind.Commented, $"MNT-{i}", $"項目 {i}", "好")).ToList();
        var mail = EmailComposer.Notification(Context(), rows);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.TextBody, Does.Contain("還有 3 則"));
            Assert.That(mail.TextBody, Does.Contain("MNT-19").And.Not.Contain("MNT-20"));
            Assert.That(mail.Subject, Does.StartWith("23 則新通知"));
        }
    }

    [Test]
    public void Test_Message_HasTheFixedSubjectAndBothBodies()
    {
        // Act
        var mail = EmailComposer.Test("team@example.com", null, "me@example.com", "https://pm.example.com");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject, Is.EqualTo("測試信：Email 通知的設定可以用了"));
            Assert.That(mail.HtmlBody, Does.Contain("<html"));
            Assert.That(mail.TextBody, Does.Contain("寄信設定"));
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    public void Notification_SubjectCutInsideASurrogatePair_NeverLeavesAHalfCharacter(int padding)
    {
        // Act：前面湊成剛好讓第 149 個字落在表情符號的中間。
        var title = new string('a', 105 + padding) + string.Concat(Enumerable.Repeat("😀", 60));
        var mail = EmailComposer.Notification(Context(), [Row(NotificationKind.Assigned, title: title)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject.Length, Is.LessThanOrEqualTo(150));
            Assert.That(() => new System.Text.UTF8Encoding(false, true).GetBytes(mail.Subject), Throws.Nothing);
            Assert.That(mail.Subject, Does.EndWith("…"));
        }
    }

    [Test]
    public void Notification_TitleWithLineBreaks_StaysOnOneLineInThePlainText()
    {
        // Act
        var mail = EmailComposer.Notification(Context(), [Row(NotificationKind.Assigned, title: "第一行\r\n第二行")]);

        // Assert
        Assert.That(mail.TextBody, Does.Contain("MNT-12 第一行 第二行").And.Not.Contain("第一行\n"));
    }

    private static NotificationRow AccountRow(AccountNotice notice) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = Guid.CreateVersion7(),
        RecipientId = Me,
        Kind = NotificationKind.AccountSecurity,
        ActorPersonId = Me,
        AccountNotice = notice,
        EventId = Guid.CreateVersion7(),
        CreatedAt = At,
    };

    private static AccountNotice Notice(AccountNoticeEvent kind) => new(kind, [], [], null, null, null, null, 0);

    [Test]
    public void AccountNotices_TokenCreated_SaysWhatHappenedAndWhereToUndoIt()
    {
        // Arrange
        var notice = Notice(AccountNoticeEvent.AccessTokenCreated) with
        {
            Tokens = [new AccountNoticeToken("CI", ["items:read", "items:write"], new DateTimeOffset(2027, 2, 23, 0, 0, 0, TimeSpan.Zero))],
            SourceAddress = "203.0.113.5",
        };

        // Act
        var mail = EmailComposer.AccountNotices(Context(), [AccountRow(notice)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject, Is.EqualTo("Kumi：你的帳號建立了存取權杖"));
            Assert.That(mail.TextBody, Does.Contain("建立了存取權杖「CI」"));
            Assert.That(mail.TextBody, Does.Contain("讀取項目、修改項目"));
            Assert.That(mail.TextBody, Does.Contain("203.0.113.5"));
            Assert.That(mail.TextBody, Does.Contain("2027/02/23"));
            Assert.That(mail.TextBody, Does.Contain("https://pm.example.com/account/integrations"));
            Assert.That(mail.TextBody, Does.Contain("不是你做的話，請到「命令列與整合」撤銷並改密碼"));
            Assert.That(mail.HtmlBody, Does.Contain("href=\"https://pm.example.com/account/integrations\""));
            Assert.That(mail.TextBody, Does.Not.Contain("/projects/"));
        }
    }

    [Test]
    public void AccountNotices_TokenWithoutExpiry_SaysItNeverExpires()
    {
        // Arrange
        var notice = Notice(AccountNoticeEvent.AccessTokenCreated) with { Tokens = [new AccountNoticeToken("舊的", ["items:read"], null)] };

        // Act
        var mail = EmailComposer.AccountNotices(Context(), [AccountRow(notice)]);

        // Assert
        Assert.That(mail.TextBody, Does.Contain("不會過期"));
    }

    [Test]
    public void AccountNotices_CliLogin_NamesTheMachineAndTheTokenCount()
    {
        // Arrange
        var notice = Notice(AccountNoticeEvent.CliLoginTokensIssued) with
        {
            Tokens = [new AccountNoticeToken("kumi（MacBook-Pro）", ["items:read"], null), new AccountNoticeToken("本機代理", ["agent:run"], null)],
            MachineName = "MacBook-Pro",
            SourceAddress = "198.51.100.9",
        };

        // Act
        var mail = EmailComposer.AccountNotices(Context(), [AccountRow(notice)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject, Is.EqualTo("Kumi：命令列登入了你的帳號"));
            Assert.That(mail.TextBody, Does.Contain("命令列在「MacBook-Pro」登入，建立了 2 把存取權杖"));
            Assert.That(mail.TextBody, Does.Contain("198.51.100.9"));
            Assert.That(mail.TextBody, Does.Contain("本機代理"));
        }
    }

    [Test]
    public void AccountNotices_OAuthAuthorization_NamesTheClientAndScopes()
    {
        // Arrange
        var notice = Notice(AccountNoticeEvent.OauthAppAuthorized) with
        {
            Scopes = ["items:read", "blueprint:write"],
            ClientName = "Claude Code",
            ClientHost = "client.example.com",
        };

        // Act
        var mail = EmailComposer.AccountNotices(Context(), [AccountRow(notice)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject, Is.EqualTo("Kumi：你的帳號授權了 AI 用戶端"));
            Assert.That(mail.TextBody, Does.Contain("授權了「Claude Code」連到你的帳號"));
            Assert.That(mail.TextBody, Does.Contain("讀取項目、修改設定"));
            Assert.That(mail.TextBody, Does.Contain("client.example.com"));
        }
    }

    [Test]
    public void AccountNotices_ReusedCode_SaysHowManyTokensWereRevoked()
    {
        // Arrange
        var notice = Notice(AccountNoticeEvent.CliLoginCodeReused) with { RevokedTokens = 2, SourceAddress = "203.0.113.5" };

        // Act
        var mail = EmailComposer.AccountNotices(Context(), [AccountRow(notice)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject, Is.EqualTo("Kumi：你的登入碼被重複使用"));
            Assert.That(mail.TextBody, Does.Contain("登入碼被重複使用，已撤銷 2 把存取權杖"));
        }
    }

    [Test]
    public void AccountNotices_Several_ListsEachUnderOneSubjectWithTheCount()
    {
        // Act
        var mail = EmailComposer.AccountNotices(
            Context(),
            [AccountRow(Notice(AccountNoticeEvent.CliLoginCodeReused)), AccountRow(Notice(AccountNoticeEvent.OauthAppAuthorized) with { ClientName = "A" })]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.Subject, Is.EqualTo("Kumi：你的帳號有 2 則安全通知"));
            Assert.That(mail.TextBody, Does.Contain("登入碼被重複使用").And.Contain("授權了「A」"));
        }
    }

    [Test]
    public void AccountNotices_NamesFromUsers_AreEscapedInHtmlAndStayOnOneLineInText()
    {
        // Arrange
        var notice = Notice(AccountNoticeEvent.AccessTokenCreated) with
        {
            Tokens = [new AccountNoticeToken("<b>x</b>\r\n第二行", ["items:read"], null)],
        };

        // Act
        var mail = EmailComposer.AccountNotices(Context(), [AccountRow(notice)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.HtmlBody, Does.Not.Contain("<b>x</b>").And.Contain("&lt;b&gt;x&lt;/b&gt;"));
            Assert.That(mail.TextBody, Does.Contain("<b>x</b> 第二行"));
            Assert.That(mail.Subject, Does.Not.Contain("\n"));
        }
    }
}
