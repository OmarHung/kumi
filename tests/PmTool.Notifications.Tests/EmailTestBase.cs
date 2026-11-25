using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>
/// 寄信的測試：每個測試各自一個資料庫與一個假時鐘，背景寄送會處理整個資料庫裡所有還在等的通知，
/// 共用的話前一個測試留下的通知會被算進這一個。
/// </summary>
public abstract class EmailTestBase
{
    protected const string SiteUrl = "https://pm.example.com";

    private NotificationsTestHost? _host;

    internal ManualClock Clock { get; private set; } = new();

    internal NotificationsTestHost Host => _host ?? throw new InvalidOperationException("模組還沒啟動。");

    internal FakeEmailTransport Transport => Host.Transport;

    /// <summary>這個測試的組織。</summary>
    protected Guid Organization { get; } = Guid.CreateVersion7();

    /// <summary>組織管理員：存設定的人。</summary>
    protected Actor Admin => new(Organization, AdminId, true);

    protected Guid AdminId { get; } = Guid.CreateVersion7();

    /// <summary>寄信一輪的時間上限；null 用預設。測試要短的時，覆寫它。</summary>
    protected virtual TimeSpan? EmailBudget => null;

    /// <summary>部署是否允許寄信主機在內網或本機；預設不允許，和正式的預設一樣。</summary>
    protected virtual bool AllowPrivateHosts => false;

    [SetUp]
    public async Task StartHostAsync()
    {
        Clock = new ManualClock();
        _host = await NotificationsTestHost.StartAsync(
            new PmTool.Abstractions.DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), null, Clock, emailBudget: EmailBudget, allowPrivateEmailHosts: AllowPrivateHosts);
    }

    [TearDown]
    public async Task StopHostAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    /// <summary>一個新的項目，和看得到它的人（每個人都有 <c>name@example.com</c> 形式的 Email）。</summary>
    internal Scene NewItem(Guid? organization = null)
    {
        var org = organization ?? Organization;
        var item = new EventBuilder(org, Guid.CreateVersion7(), Guid.CreateVersion7());
        Guid[] everyone = [.. Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7())];
        Host.Access.Allow(item.ProjectId, everyone);
        for (var i = 0; i < everyone.Length; i++)
        {
            Host.People.Add(org, everyone[i], $"Person {i}", $"person{i}-{everyone[i]:N}@example.com");
        }

        return new Scene(item, everyone);
    }

    protected string EmailOf(Guid personId) => Host.People.EmailOf(Organization, personId);

    /// <summary>把事件在現在這個時間交給通知讀者（通知的建立時間就是事件發生的時間）。</summary>
    internal Task HandleAsync(params WorkEvent[] events) =>
        Host.Consumer.HandleAsync([.. events.Select(e => e with { OccurredAt = Clock.GetUtcNow() })], CancellationToken.None);

    /// <summary>用 Gmail 開啟寄信。</summary>
    protected async Task EnableEmailAsync(EmailSettingsInput? input = null)
    {
        var result = await Host.EmailSettings.SaveAsync(
            Admin, input ?? GmailInput(), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    protected static EmailSettingsInput GmailInput(bool enabled = true) =>
        new(enabled, EmailProvider.Gmail, null, null, null, "team@example.com", "abcd efgh ijkl mnop", null, "Example 公司", SiteUrl);

    protected static EmailSettingsInput SmtpInput(bool enabled = true, string? password = null) => new(
        enabled, EmailProvider.Smtp, "smtp.example.com", 587, SmtpSecurity.StartTls, "mailer", password ?? "secret", "noreply@example.com", "Example 公司", SiteUrl);

    protected Task<int> DispatchAsync() => Host.Dispatcher.DispatchAsync(CancellationToken.None);

    protected void Wait(TimeSpan by) => Clock.Advance(by);

    /// <summary>讓通知超過「等多久才寄」的延遲。</summary>
    protected void PassTheDelay() => Clock.Advance(EmailRules.Delay + TimeSpan.FromSeconds(1));
}
