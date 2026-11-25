using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>
/// 機器憑證換新與重新配對、或兩個登記交疊時的競賽（安全修復第四輪審查 M-1、L-1）。
/// 用一個沒提交的交易鎖住代理的資料列：登記先讀到舊值、卡在第一個更新，等交易提交之後才繼續，交疊就是確定的。
/// </summary>
[TestFixture]
public sealed class HostCredentialRaceTests
{
    private static readonly TimeSpan Overlap = TimeSpan.FromMilliseconds(600);

    private HostHarness _h = null!;

    [SetUp]
    public async Task SetUp() => _h = await HostHarness.StartAsync();

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private async Task<HostAgentRow> RowAsync(Guid agentId)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.HostAgents.AsNoTracking().SingleAsync(a => a.Id == agentId);
    }

    [Test]
    public async Task Rotation_overlapping_a_repairing_does_not_hand_out_a_usable_next_credential()
    {
        // Arrange
        var worker = await _h.PairAsync();
        _h.Clock.Advance(TimeSpan.FromDays(70));
        await using var locker = await _h.Host.CreateDbContextAsync();
        await using var transaction = await locker.Database.BeginTransactionAsync();
        await locker.HostAgents.Where(a => a.Id == worker.AgentId).ExecuteUpdateAsync(
            set => set.SetProperty(a => a.CredentialHash, "repaired-hash"));

        // Act：重新配對換掉了憑證但還沒提交：登記讀到舊的憑證、卡在第一個更新，提交之後才繼續。
        var checkIn = _h.CheckInAsync(worker);
        await Task.Delay(Overlap);
        await transaction.CommitAsync();
        var result = await checkIn;

        // Assert
        Assert.That(result.RotatedCredential, Is.Null);
        var row = await RowAsync(worker.AgentId);
        Assert.That(row.NextCredentialHash, Is.Null);
        Assert.That(row.CredentialHash, Is.EqualTo("repaired-hash"));
    }

    [Test]
    public async Task Two_simultaneous_check_ins_near_expiry_give_out_one_credential_and_it_is_valid()
    {
        // Arrange
        var worker = await _h.PairAsync();
        _h.Clock.Advance(TimeSpan.FromDays(70));
        await using var locker = await _h.Host.CreateDbContextAsync();
        await using var transaction = await locker.Database.BeginTransactionAsync();
        await locker.HostAgents.Where(a => a.Id == worker.AgentId).ExecuteUpdateAsync(
            set => set.SetProperty(a => a.AgentVersion, "hold"));

        // Act
        var first = _h.CheckInAsync(worker);
        var second = _h.CheckInAsync(worker);
        await Task.Delay(Overlap);
        await transaction.CommitAsync();
        var results = await Task.WhenAll(first, second);

        // Assert
        var issued = results.Select(r => r.RotatedCredential).Where(c => c is not null).ToList();
        Assert.That(issued, Has.Count.EqualTo(1));
        var authenticated = await _h.Worker.AuthenticateAsync(issued[0]!, HostHarness.DefaultMachineKey, default);
        Assert.That(authenticated, Is.EqualTo(worker.Identity));
    }

    [Test]
    public async Task A_machine_mismatch_recorded_after_a_repairing_does_not_overwrite_the_new_pairings_warning_state()
    {
        // Arrange
        var worker = await _h.PairAsync();
        await using var locker = await _h.Host.CreateDbContextAsync();
        await using var transaction = await locker.Database.BeginTransactionAsync();
        await locker.HostAgents.Where(a => a.Id == worker.AgentId).ExecuteUpdateAsync(
            set => set.SetProperty(a => a.CredentialHash, "repaired-hash"));

        // Act：另一台電腦的重新配對換掉了憑證但還沒提交：登記讀到舊的憑證、卡在第一個更新，提交之後才繼續。
        var other = _h.Worker.CheckInAsync(
            worker.Identity, new HostAgentCheckIn("0.9.0", HostHarness.ServerUrl, []) { MachineKey = "other-machine-9", MachineName = "別台" }, 0, default);
        await Task.Delay(Overlap);
        await transaction.CommitAsync();
        var result = await other;

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.HostMachineMismatch));
        Assert.That((await RowAsync(worker.AgentId)).SuspectMachineKey, Is.Null);
    }
}
