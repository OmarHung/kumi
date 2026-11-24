namespace PmTool.Notifications.Tests;

/// <summary>
/// 同一個類別的測試共用一個資料庫與一個模組；每個測試用自己的項目、專案與人（編號都是新的），不依賴別的測試留下的資料。
/// </summary>
public abstract class ConsumerTestBase
{
    private NotificationsTestHost? _host;

    internal NotificationsTestHost Host => _host ?? throw new InvalidOperationException("模組還沒啟動。");

    /// <summary>要用假時鐘的測試類別覆寫；預設用系統時鐘。</summary>
    protected virtual TimeProvider? Clock => null;

    [OneTimeSetUp]
    public async Task StartModuleAsync() => _host = await NotificationsTestHost.StartAsync(Clock);

    [OneTimeTearDown]
    public async Task StopModuleAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    /// <summary>一個新的項目，和一群都看得到它的專案的人。</summary>
    internal Scene NewItem()
    {
        var item = EventBuilder.ForNewItem();
        Guid[] everyone = [.. Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7())];
        Host.Access.Allow(item.ProjectId, everyone);
        return new Scene(item, everyone);
    }

    internal Task HandleAsync(params PmTool.Work.WorkEvent[] events) =>
        Host.Consumer.HandleAsync(events, CancellationToken.None);
}

/// <summary>一個項目與四個看得到它的人，用 <c>var (item, a, b, c, d) = NewItem()</c> 拆開。</summary>
internal sealed record Scene(EventBuilder Item, Guid[] People)
{
    public void Deconstruct(out EventBuilder item, out Guid first, out Guid second, out Guid third, out Guid fourth)
    {
        item = Item;
        (first, second, third, fourth) = (People[0], People[1], People[2], People[3]);
    }
}
