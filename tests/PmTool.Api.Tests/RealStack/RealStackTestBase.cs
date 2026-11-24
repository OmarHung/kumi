namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 每個測試類別一個自己的資料庫與一個應用程式實體，測試類別之間不互相影響。
/// 同一個類別裡的測試共用它們，所以每個測試開自己的專案（代號不同），不依賴別的測試留下的資料。
/// </summary>
public abstract class RealStackTestBase
{
    private RealStackFactory? _factory;

    internal RealStackFactory Factory => _factory ?? throw new InvalidOperationException("應用程式還沒啟動。");

    /// <summary>要用特別的設定啟動的測試類別覆寫（例如打開即時連線）；預設用 <see cref="RealStackFactory"/> 的預設值。</summary>
    internal virtual RealStackOptions? FactoryOptions => null;

    [OneTimeSetUp]
    public async Task StartApplicationAsync()
    {
        _factory = new RealStackFactory(await RealStackDatabase.CreateDatabaseAsync(), FactoryOptions);

        // 啟動時才會遷移資料表、建立第一個擁有者；在這裡做完，第一個測試量到的時間才不會包含它。
        _ = _factory.Server;
    }

    [OneTimeTearDown]
    public void StopApplication() => _factory?.Dispose();

    /// <summary>用設定裡的擁有者登入。</summary>
    internal Task<SignedInClient> SignInAsOwnerAsync() =>
        SignInAsAsync(RealStackFactory.OwnerEmail, RealStackFactory.OwnerPassword);

    internal async Task<SignedInClient> SignInAsAsync(string email, string password)
    {
        var client = Factory.CreateApiClient();
        var user = await RealStackSteps.SignInAsync(client, email, password);
        return new SignedInClient(client, user["personId"]!.GetValue<Guid>());
    }

    /// <summary>新增組織成員、加進專案、用他登入。會用掉一次登入的額度。</summary>
    internal async Task<SignedInClient> AddMemberAsync(
        SignedInClient owner, string projectKey, string role, string displayName, string email)
    {
        var person = await RealStackSteps.CreateMemberAsync(owner.Client, displayName, email);
        await RealStackSteps.SetMemberAsync(owner.Client, projectKey, person["personId"]!.GetValue<Guid>(), role);
        return await SignInAsAsync(email, RealStackSteps.MemberPassword);
    }
}
