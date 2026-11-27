using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.HostAgents;
using PmTool.Ai.Tests.LocalAgents;

namespace PmTool.Ai.Tests.Proposals;

/// <summary>
/// 本機代理與主機代理的工作：三個新工具只放給 kumi 0.17.0 以上（舊版會整件工作不跑），提示拿掉那幾行並請使用者更新；
/// 兩條路的系統提示都帶「現在」那一句。
/// </summary>
[TestFixture]
public sealed class RevisionAgentTests
{
    private static readonly string[] RevisionTools = ["withdraw_proposal", "propose_item_deletions", "propose_bulk_item_changes"];

    private const string NewVersion = AiRules.MinAgentVersionForRevisionTools;

    [TestCase("0.16.0", false)]
    [TestCase(NewVersion, true)]
    public async Task LocalJob_HasTheRevisionToolsOnlyForAKumiThatIsNewEnough(string version, bool hasTools)
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = version;
        h.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();
        var engine = await h.RegisterAsync();
        await h.SendAsync(engine, "把到期日都往後延");

        // Act
        var job = await h.ClaimAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RevisionTools.All(job.AllowedTools.Contains), Is.EqualTo(hasTools));
            Assert.That(RevisionTools.Any(job.AllowedTools.Contains), Is.EqualTo(hasTools));
            Assert.That(job.SystemPrompt.Contains("- propose_bulk_item_changes："), Is.EqualTo(hasTools));
            Assert.That(job.SystemPrompt.Contains("這一次沒有 withdraw_proposal、propose_item_deletions、propose_bulk_item_changes"), Is.EqualTo(!hasTools));
            Assert.That(job.Prompt, Does.Contain("現在是 2026-11-18（星期三）16:00，時區 Asia/Taipei"));
            Assert.That(job.SystemPrompt, Does.Not.Contain("現在是 20"));
        }
    }

    [TestCase("0.16.0", false)]
    [TestCase(NewVersion, true)]
    public async Task HostJob_HasTheRevisionToolsOnlyForAWorkerThatIsNewEnough(string version, bool hasTools)
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        h.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();
        var setup = await h.SetUpAsync();
        setup.Worker.AgentVersion = version;
        await h.CheckInAsync(setup.Worker);
        await h.SendAsync(h.Member, setup.Source, "把到期日都往後延");

        // Act
        var job = await h.ClaimAsync(setup.Worker);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RevisionTools.All(job.AllowedTools.Contains), Is.EqualTo(hasTools));
            Assert.That(job.SystemPrompt.Contains("- withdraw_proposal："), Is.EqualTo(hasTools));
            Assert.That(
                job.SystemPrompt.Contains("這一次沒有 withdraw_proposal、propose_item_deletions、propose_bulk_item_changes") && job.SystemPrompt.Contains("請組織擁有者更新主機的 kumi"),
                Is.EqualTo(!hasTools));
            Assert.That(job.Prompt, Does.Contain("現在是 2026-11-18（星期三）16:00，時區 Asia/Taipei"));
            Assert.That(job.SystemPrompt, Does.Not.Contain("現在是 20"));
        }
    }
}
