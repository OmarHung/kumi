using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Notifications.Tests;

/// <summary>帳號通知：身分模組發出的帳號事件，變成本人收件匣裡沒有專案的一則通知。</summary>
[TestFixture]
public sealed class AccountNoticeTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 25, 8, 0, 0, TimeSpan.Zero);

    private RecordingObserver _observer = null!;
    private NotificationsTestHost _host = null!;
    private IAccountEventSink _sink = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _observer = new RecordingObserver();
        _host = await NotificationsTestHost.StartAsync(
            new DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), observers: [_observer]);
        _sink = _host.Services.GetServices<IAccountEventSink>().Single();
    }

    [TearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private static Actor As(Guid organization, Guid person) => new(organization, person, false);

    private static AccountEvent TokenCreated(Guid organization, Guid person, Guid? eventId = null) =>
        new(eventId ?? Guid.CreateVersion7(), organization, person, AccountEventKind.AccessTokenCreated, At)
        {
            Tokens = [new AccountEventToken("CI", [AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite], At.AddDays(90))],
            SourceAddress = "203.0.113.5",
        };

    private async Task<NotificationItem> OnlyNoticeAsync(Guid organization, Guid person)
    {
        var page = (await _host.Inbox.ListAsync(As(organization, person), new NotificationQuery(false, null, 50), CancellationToken.None)).Value;
        return page.Items.Single();
    }

    [Test]
    public async Task Publish_TokenCreated_AddsOneInboxNoticeWithoutProjectOrItem()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        // Act
        await _sink.PublishAsync(TokenCreated(organization, person), CancellationToken.None);

        // Assert
        var notice = await OnlyNoticeAsync(organization, person);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notice.Kind, Is.EqualTo(NotificationKind.AccountSecurity));
            Assert.That(notice.ProjectKey, Is.Null);
            Assert.That(notice.ItemKey, Is.Null);
            Assert.That(notice.ItemTitle, Is.Null);
            Assert.That(notice.ActorPersonId, Is.EqualTo(person));
            Assert.That(notice.ReadAt, Is.Null);
            Assert.That(notice.CreatedAt, Is.EqualTo(At));
            Assert.That(notice.Account!.Event, Is.EqualTo(AccountNoticeEvent.AccessTokenCreated));
            Assert.That(notice.Account.SourceAddress, Is.EqualTo("203.0.113.5"));
            Assert.That(notice.Account.Tokens.Single().Name, Is.EqualTo("CI"));
            Assert.That(notice.Account.Tokens.Single().Scopes, Is.EqualTo(new[] { "items:read", "items:write" }));
            Assert.That(notice.Account.Tokens.Single().ExpiresAt, Is.EqualTo(At.AddDays(90)));
        }
    }

    [Test]
    public async Task Publish_SameEventTwice_LeavesOneNoticeAndTellsTheObserverOnce()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var accountEvent = TokenCreated(organization, person);

        // Act
        await _sink.PublishAsync(accountEvent, CancellationToken.None);
        await _sink.PublishAsync(accountEvent, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _host.AllNotificationsAsync(), Has.Count.EqualTo(1));
            Assert.That(_observer.Calls, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Inbox_PersonWithoutAnyProject_SeesTheNoticeAndItCountsAsUnread()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await _sink.PublishAsync(TokenCreated(organization, person), CancellationToken.None);

        // Act
        var unread = await _host.Inbox.CountUnreadAsync(As(organization, person), CancellationToken.None);
        var onlyUnread = (await _host.Inbox.ListAsync(As(organization, person), new NotificationQuery(true, null, 50), CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unread, Is.EqualTo(1));
            Assert.That(onlyUnread.Items, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Inbox_NoticeOfSomeoneElse_IsNotListedAndNotCounted()
    {
        // Arrange
        var (organization, owner, other) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await _sink.PublishAsync(TokenCreated(organization, owner), CancellationToken.None);

        // Act
        var page = (await _host.Inbox.ListAsync(As(organization, other), new NotificationQuery(false, null, 50), CancellationToken.None)).Value;
        var otherOrganization = await _host.Inbox.CountUnreadAsync(As(Guid.CreateVersion7(), owner), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items, Is.Empty);
            Assert.That(await _host.Inbox.CountUnreadAsync(As(organization, other), CancellationToken.None), Is.Zero);
            Assert.That(otherOrganization, Is.Zero);
        }
    }

    [Test]
    public async Task Inbox_AfterLeavingAProject_ItemNotificationsDisappearButTheAccountNoticeStays()
    {
        // Arrange
        var item = EventBuilder.ForNewItem();
        var (creator, me) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        _host.Access.Allow(item.ProjectId, creator, me);
        await _host.Consumer.HandleAsync([item.Created(creator, me)], CancellationToken.None);
        await _sink.PublishAsync(TokenCreated(item.OrganizationId, me), CancellationToken.None);
        var actor = As(item.OrganizationId, me);
        var before = await _host.Inbox.CountUnreadAsync(actor, CancellationToken.None);

        // Act
        _host.Access.Revoke(item.ProjectId, me);
        var after = (await _host.Inbox.ListAsync(actor, new NotificationQuery(false, null, 50), CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.EqualTo(2));
            Assert.That(after.Items.Select(n => n.Kind), Is.EqualTo(new[] { NotificationKind.AccountSecurity }));
            Assert.That(await _host.Inbox.CountUnreadAsync(actor, CancellationToken.None), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Inbox_HiddenItemOfAnOwnOnlyProject_StaysHiddenWhileTheAccountNoticeStays()
    {
        // Arrange
        var item = EventBuilder.ForNewItem();
        var (creator, me) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        _host.Access.AllowOwnOnly(item.ProjectId, me);
        _host.Access.Allow(item.ProjectId, creator);
        await _host.Consumer.HandleAsync([item.Created(creator, me)], CancellationToken.None);
        await _sink.PublishAsync(TokenCreated(item.OrganizationId, me), CancellationToken.None);
        _host.Access.HideItem(me, item.ItemId);

        // Act
        var page = (await _host.Inbox.ListAsync(As(item.OrganizationId, me), new NotificationQuery(false, null, 50), CancellationToken.None)).Value;

        // Assert
        Assert.That(page.Items.Select(n => n.Kind), Is.EqualTo(new[] { NotificationKind.AccountSecurity }));
    }

    [Test]
    public async Task MarkRead_TheAccountNotice_ClearsItFromTheUnreadCount()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await _sink.PublishAsync(TokenCreated(organization, person), CancellationToken.None);
        await _sink.PublishAsync(TokenCreated(organization, person), CancellationToken.None);
        var ids = (await _host.AllNotificationsAsync()).Select(n => n.Id).ToArray();

        // Act
        var one = await _host.Inbox.MarkReadAsync(As(organization, person), [ids[0]], CancellationToken.None);
        var rest = await _host.Inbox.MarkAllReadAsync(As(organization, person), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(one, Is.EqualTo(1));
            Assert.That(rest, Is.EqualTo(1));
            Assert.That(await _host.Inbox.CountUnreadAsync(As(organization, person), CancellationToken.None), Is.Zero);
        }
    }

    [Test]
    public async Task Publish_AnyKind_TellsTheObserverWhoseInboxChanged()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        // Act
        await _sink.PublishAsync(TokenCreated(organization, person), CancellationToken.None);

        // Assert
        var call = _observer.Calls.Single();
        Assert.That(call, Is.EqualTo(new[] { new NotifiedRecipient(organization, person) }));
    }

    [Test]
    public async Task Publish_TheNoticeIsAlreadyStoredWhenTheObserverIsCalled()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var seen = -1;
        _observer.OnNotified = async () => seen = await _host.Inbox.CountUnreadAsync(As(organization, person), CancellationToken.None);

        // Act
        await _sink.PublishAsync(TokenCreated(organization, person), CancellationToken.None);

        // Assert
        Assert.That(seen, Is.EqualTo(1));
    }

    [Test]
    public async Task Publish_ObserverThatThrows_KeepsTheNoticeDoesNotThrowAndLogsAWarning()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        _observer.OnNotified = () => throw new InvalidOperationException("推送壞了");

        // Act
        await _sink.PublishAsync(TokenCreated(organization, person), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _host.AllNotificationsAsync(), Has.Count.EqualTo(1));
            Assert.That(_host.Logs.Entries.Where(e => e.Level == LogLevel.Warning && e.Exception is InvalidOperationException), Is.Not.Empty);
        }
    }

    [Test]
    public async Task Publish_WhenTheDatabaseRefuses_DoesNotThrowAndLogsAWarning()
    {
        // Arrange：事件的種類不認得，寫不進去。
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var broken = TokenCreated(organization, person) with { Kind = (AccountEventKind)99 };

        // Act
        await _sink.PublishAsync(broken, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _host.AllNotificationsAsync(), Is.Empty);
            Assert.That(_host.Logs.Entries.Where(e => e.Level == LogLevel.Warning), Is.Not.Empty);
        }
    }

    [Test]
    public async Task Publish_CliLoginTokens_KeepsEveryTokenTheMachineAndTheSource()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var accountEvent = new AccountEvent(Guid.CreateVersion7(), organization, person, AccountEventKind.CliLoginTokensIssued, At)
        {
            Tokens =
            [
                new AccountEventToken("kumi（MacBook-Pro）", [AccessTokenScope.ItemsRead], null),
                new AccountEventToken("kumi 本機代理（MacBook-Pro）", [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead], At.AddDays(30)),
            ],
            MachineName = "MacBook-Pro",
            SourceAddress = "2001:db8::7",
        };

        // Act
        await _sink.PublishAsync(accountEvent, CancellationToken.None);

        // Assert
        var account = (await OnlyNoticeAsync(organization, person)).Account!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(account.Event, Is.EqualTo(AccountNoticeEvent.CliLoginTokensIssued));
            Assert.That(account.Tokens.Select(t => t.Name), Is.EqualTo(new[] { "kumi（MacBook-Pro）", "kumi 本機代理（MacBook-Pro）" }));
            Assert.That(account.Tokens[0].ExpiresAt, Is.Null);
            Assert.That(account.Tokens[1].Scopes, Is.EqualTo(new[] { "agent:run", "items:read" }));
            Assert.That(account.MachineName, Is.EqualTo("MacBook-Pro"));
            Assert.That(account.SourceAddress, Is.EqualTo("2001:db8::7"));
        }
    }

    [Test]
    public async Task Publish_OAuthAuthorization_KeepsTheClientAndScopes()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var accountEvent = new AccountEvent(Guid.CreateVersion7(), organization, person, AccountEventKind.OAuthAppAuthorized, At)
        {
            Scopes = [AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead],
            ClientName = "Claude Code",
            ClientHost = "client.example.com",
        };

        // Act
        await _sink.PublishAsync(accountEvent, CancellationToken.None);

        // Assert
        var account = (await OnlyNoticeAsync(organization, person)).Account!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(account.Event, Is.EqualTo(AccountNoticeEvent.OauthAppAuthorized));
            Assert.That(account.Scopes, Is.EqualTo(new[] { "items:read", "blueprint:read" }));
            Assert.That(account.ClientName, Is.EqualTo("Claude Code"));
            Assert.That(account.ClientHost, Is.EqualTo("client.example.com"));
            Assert.That(account.Tokens, Is.Empty);
        }
    }

    [Test]
    public async Task Publish_ReusedLoginCode_KeepsTheRevokedCount()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var accountEvent = new AccountEvent(Guid.CreateVersion7(), organization, person, AccountEventKind.CliLoginCodeReused, At)
        {
            RevokedTokens = 2,
            SourceAddress = "198.51.100.9",
        };

        // Act
        await _sink.PublishAsync(accountEvent, CancellationToken.None);

        // Assert
        var account = (await OnlyNoticeAsync(organization, person)).Account!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(account.Event, Is.EqualTo(AccountNoticeEvent.CliLoginCodeReused));
            Assert.That(account.RevokedTokens, Is.EqualTo(2));
            Assert.That(account.SourceAddress, Is.EqualTo("198.51.100.9"));
        }
    }

    [Test]
    public async Task A_new_local_agent_machine_becomes_a_notice_with_the_machine_name_and_source()
    {
        // Arrange
        var (organization, person) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var accountEvent = new AccountEvent(Guid.CreateVersion7(), organization, person, AccountEventKind.LocalAgentMachineAdded, At)
        {
            MachineName = "辦公室的 Mac",
            SourceAddress = "198.51.100.9",
        };

        // Act
        await _sink.PublishAsync(accountEvent, CancellationToken.None);

        // Assert
        var account = (await OnlyNoticeAsync(organization, person)).Account!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(account.Event, Is.EqualTo(AccountNoticeEvent.LocalAgentMachineAdded));
            Assert.That(account.MachineName, Is.EqualTo("辦公室的 Mac"));
            Assert.That(account.SourceAddress, Is.EqualTo("198.51.100.9"));
            Assert.That(account.Tokens, Is.Empty);
        }
    }

    private sealed class RecordingObserver : INotificationObserver
    {
        private readonly List<IReadOnlyCollection<NotifiedRecipient>> _calls = [];

        public Func<Task>? OnNotified { get; set; }

        public IReadOnlyList<IReadOnlyCollection<NotifiedRecipient>> Calls
        {
            get
            {
                lock (_calls)
                {
                    return [.. _calls];
                }
            }
        }

        public async Task NotifiedAsync(IReadOnlyCollection<NotifiedRecipient> recipients, CancellationToken cancellationToken)
        {
            lock (_calls)
            {
                _calls.Add(recipients);
            }

            if (OnNotified is { } callback)
            {
                await callback();
            }
        }
    }
}
