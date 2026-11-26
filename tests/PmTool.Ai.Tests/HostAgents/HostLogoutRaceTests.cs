namespace PmTool.Ai.Tests.HostAgents;

/// <summary>
/// 登出要求（<c>host_agents.logout_requested</c>）同時被擁有者加入、被 worker 的登記移除時不能互相蓋掉：
/// 擁有者按了登出、剛好 worker 在登記，那次登出不能不見（按了沒反應）。
/// 時間點靠多跑幾輪碰撞，不保證每一輪都撞到；修好之後每一輪都必須成立。
/// </summary>
[TestFixture]
public sealed class HostLogoutRaceTests
{
    private const int Rounds = 60;

    private HostHarness _h = null!;
    private HostSetup _setup = null!;

    [SetUp]
    public async Task SetUp()
    {
        _h = await HostHarness.StartAsync();
        _setup = await _h.SetUpAsync();
    }

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private void Report(bool codexLoggedIn) => _setup.Worker.Reports =
    [
        HostHarness.Report(loginId: _setup.LoginId),
        HostHarness.Report(kind: AiProviderKind.Codex, loggedIn: codexLoggedIn),
    ];

    [Test]
    public async Task Logout_requested_while_the_worker_is_checking_in_is_not_lost()
    {
        for (var round = 0; round < Rounds; round++)
        {
            // Arrange：Codex 沒有登出要求（回報沒登入就清掉），然後 Codex 登入著。
            Report(codexLoggedIn: false);
            await _h.CheckInAsync(_setup.Worker);
            Report(codexLoggedIn: true);

            // Act：擁有者按登出與 worker 登記同時發生。
            // 擁有者那邊的交易比較長：每輪錯開不同的毫秒數，讓登記的「讀」落在它提交之前、「寫」落在之後。
            var logout = _h.Service.LogoutAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.Codex, default);
            await Task.Delay(round % 15);
            var checkIn = _h.CheckInAsync(_setup.Worker);
            await Task.WhenAll(logout, checkIn);

            // Assert
            var codex = (await _h.AgentAsync(_setup.Worker.AgentId)).Engines.Single(e => e.Kind == AiProviderKind.Codex);
            Assert.That(codex.LogoutPending, Is.True, $"第 {round + 1} 輪：登出要求被同時的登記蓋掉了。");
        }
    }
}
