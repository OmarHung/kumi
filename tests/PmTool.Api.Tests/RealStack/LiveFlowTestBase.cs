using System.Net;
using PmTool.Api.Tests.Support;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Live;
using PmTool.Api.Tests.Live;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 即時推送的端到端測試共用的部分：打開即時連線、每個測試從沒有連線開始、連線與等群組的步驟。
/// 讀事件與重新檢查連線都由測試直接跑一輪，不靠計時器。
/// </summary>
public abstract class LiveFlowTestBase : RealStackTestBase
{
    internal LiveUser Owner { get; private set; } = null!;

    internal LiveUser Editor { get; private set; } = null!;

    internal override RealStackOptions? FactoryOptions => new()
    {
        Settings = new Dictionary<string, string?>
        {
            [LiveRegistration.EnabledSetting] = "true",
            [LiveUpdateWorker.EnabledSetting] = "false",
        },
    };

    [OneTimeSetUp]
    public async Task SignInOwnerAndEditorAsync()
    {
        Owner = await LiveUser.SignInAsync(Factory, RealStackFactory.OwnerEmail, RealStackFactory.OwnerPassword);
        Editor = await NewMemberAsync("editor");
    }

    /// <summary>
    /// 每個測試從沒有連線開始：沒有連線時讀事件的工作只把位置更新到最新，
    /// 不會把上一個測試最後寫的事件當成新的推出來。
    /// </summary>
    [SetUp]
    public async Task StartWithNoConnectionsAsync()
    {
        await WaitForNoConnectionsAsync();
        await Factory.RunLiveOnceAsync();
    }

    [OneTimeTearDown]
    public void DisposeOwnerAndEditor()
    {
        Owner?.Dispose();
        Editor?.Dispose();
    }

    internal static string?[] Keys((string Name, JsonElement Payload) message) =>
        [.. message.Payload.GetProperty("itemKeys").EnumerateArray().Select(k => k.GetString()).Order(StringComparer.Ordinal)];

    internal async Task<LiveUser> NewMemberAsync(string name)
    {
        var email = RealStackSteps.UniqueEmail(name);
        await RealStackSteps.CreateMemberAsync(Owner.Client, name, email);
        return await LiveUser.SignInAsync(Factory, email, RealStackSteps.MemberPassword);
    }

    /// <summary>新專案：E 是編輯者，另外可以指定別的成員。</summary>
    internal async Task<Guid> StartProjectAsync(string key, params (LiveUser Who, string Role)[] others)
    {
        var project = await RealStackSteps.CreateProjectAsync(Owner.Client, key, "simple_tasks");
        await RealStackSteps.SetMemberAsync(Owner.Client, key, Editor.PersonId, "editor");
        foreach (var (who, role) in others)
        {
            await RealStackSteps.SetMemberAsync(Owner.Client, key, who.PersonId, role);
        }

        return project["id"]!.GetValue<Guid>();
    }

    /// <param name="groups">連線進來之後，這個人的連線要已經加進哪些群組（自己的收件匣的群組一定有，不用列）。</param>
    internal async Task<LiveHubClient> ConnectAsync(
        LiveUser who, string[]? groups = null, HttpTransportType transport = HttpTransportType.LongPolling, bool runBefore = true)
    {
        // 登記連線之前先跑一輪：這時如果沒有別的連線，位置就更新到最新，連線之前寫的事件不會被當成新的。
        if (runBefore)
        {
            await Factory.RunLiveOnceAsync();
        }

        var client = LiveHubClient.Create(Factory.Server, who.Cookie, transport);
        await client.StartAsync();
        await WaitForGroupsAsync(who.PersonId, [LiveGroups.Person(who.PersonId), .. groups ?? []]);
        return client;
    }

    /// <summary>連線建立與加入群組在握手之後才完成，測試要等到群組齊了才開始，不然會漏掉訊息。</summary>
    internal async Task WaitForGroupsAsync(Guid personId, string[] expected, bool exactly = false)
    {
        var connections = Factory.Services.GetRequiredService<LiveConnections>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var mine = connections.Snapshot().Where(c => c.PersonId == personId).ToArray();
            if (mine.Length > 0 && mine.All(c => expected.All(c.Groups.Contains) && (!exactly || c.Groups.Count == expected.Length)))
            {
                return;
            }

            await Task.Delay(25);
        }

        var actual = connections.Snapshot().Where(c => c.PersonId == personId).Select(c => string.Join("|", c.Groups));
        Assert.Fail($"連線的群組沒有變成 {string.Join("|", expected)}：{string.Join(", ", actual)}");
    }

    internal async Task WaitForNoConnectionsAsync()
    {
        var connections = Factory.Services.GetRequiredService<LiveConnections>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (connections.Snapshot().Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.That(connections.Snapshot(), Is.Empty);
    }

    internal async Task WaitForNoConnectionsOfAsync(Guid personId)
    {
        var connections = Factory.Services.GetRequiredService<LiveConnections>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (connections.CountOf(personId) > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.That(connections.CountOf(personId), Is.Zero);
    }
}

/// <summary>登入之後的人：Cookie（給 SignalR）與帶著它的用戶端（給一般請求）。</summary>
internal sealed class LiveUser : IDisposable
{
    private LiveUser(string cookie, Guid personId, HttpClient client)
    {
        Cookie = cookie;
        PersonId = personId;
        Client = client;
    }

    public string Cookie { get; }

    public Guid PersonId { get; }

    public HttpClient Client { get; }

    /// <summary>登入一次，同時拿到工作階段的 Cookie 與人員編號；之後的一般請求自己帶這個 Cookie，不靠用戶端記。</summary>
    public static async Task<LiveUser> SignInAsync(RealStackFactory factory, string email, string password)
    {
        var client = Sessions.CreateReplayClient(factory);
        using var response = await client.PostAsync(Sessions.Route, Sessions.Credentials(email, password));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var cookie = Sessions.SessionCookieOf(response) ?? throw new AssertionException("登入成功卻沒有發工作階段的 Cookie");
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return new LiveUser(cookie, data!["user"]!["personId"]!.GetValue<Guid>(), client);
    }

    public void Dispose() => Client.Dispose();
}
