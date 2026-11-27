using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.HostAgents;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>八個專案讀寫工具：工具清單與提示都跟著 kumi 的版本（0.20.0 以上才有），舊版的提示拿掉那幾行並請使用者更新。</summary>
[TestFixture]
public sealed class ProjectWritesVersionTests
{
    private static readonly string[] EightTools =
    [
        "list_comments", "list_deleted_items", "get_project_info", "get_planning",
        "propose_planning_changes", "propose_comment_changes", "propose_item_restores", "propose_project_changes",
    ];

    [Test]
    public void ToolLists_OnlyProjectConversationsOnANewEnoughKumiGetTheEightTools()
    {
        // Act
        var oldProject = AgentToolLists.For("WEB", AiRules.MinAgentVersionForFindItems);
        var newProject = AgentToolLists.For("WEB", AiRules.MinAgentVersionForProjectWrites);
        var newOrganization = AgentToolLists.For(null, AiRules.MinAgentVersionForProjectWrites);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldProject, Does.Contain("find_items"));
            Assert.That(oldProject.Intersect(EightTools), Is.Empty);
            Assert.That(newProject, Is.SupersetOf(EightTools));
            Assert.That(newOrganization.Intersect(EightTools), Is.Empty, "組織對話不加這八個工具");
            Assert.That(AgentToolLists.SupportsProjectWrites(AiRules.MinAgentVersionForFindItems), Is.False);
            Assert.That(AgentToolLists.SupportsProjectWrites(AiRules.MinAgentVersionForProjectWrites), Is.True);
            Assert.That(AgentToolLists.SupportsProjectWrites(null), Is.False);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AgentPrompts_OnANewKumi_ListTheEightToolsWithTheirParameters(bool host)
    {
        // Act
        var prompt = host ? LocalPromptBuilder.HostSystemPrompt("WEB", []) : LocalPromptBuilder.SystemPrompt("WEB", []);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in EightTools)
            {
                Assert.That(prompt, Does.Contain($"- {tool}："), tool);
            }

            Assert.That(prompt, Does.Contain("replyTo").And.Contain("parentKey").And.Contain("cycleId").And.Contain("milestoneId"));
            Assert.That(prompt, Does.Not.Contain("這一次沒有 list_comments"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AgentPrompts_OnAKumiThatIsTooOld_DropTheEightLinesAndSayToUpdate(bool host)
    {
        // Act
        var prompt = host
            ? LocalPromptBuilder.HostSystemPrompt("WEB", [], supportsProjectWrites: false)
            : LocalPromptBuilder.SystemPrompt("WEB", [], supportsProjectWrites: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in EightTools)
            {
                Assert.That(prompt, Does.Not.Contain($"- {tool}："), tool);
            }

            Assert.That(prompt, Does.Contain("這一次沒有 list_comments").And.Contain("8 個工具").And.Contain(host ? "更新主機的 kumi" : "kumi update"));
            Assert.That(prompt, Does.Contain("- get_item：").And.Contain("- find_items："), "其他工具照舊");
        }
    }

    [Test]
    public void OrganizationPrompts_NeverListTheEightTools()
    {
        // Act
        var local = LocalPromptBuilder.OrganizationSystemPrompt(true);
        var host = LocalPromptBuilder.HostOrganizationSystemPrompt(true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in EightTools)
            {
                Assert.That(local, Does.Not.Contain($"- {tool}："));
                Assert.That(host, Does.Not.Contain($"- {tool}："));
            }

            Assert.That(local, Does.Not.Contain("這一次沒有 list_comments"));
        }
    }

    [Test]
    public void SystemPrompt_TellsTheModelWhichToolToUseAndNoLongerSaysThesePartsCannotBeDone()
    {
        // Act
        var prompt = AssistantPrompt.SystemPrompt;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("propose_planning_changes").And.Contain("propose_comment_changes")
                .And.Contain("propose_item_restores").And.Contain("propose_project_changes"));
            Assert.That(prompt, Does.Contain("list_comments").And.Contain("list_deleted_items").And.Contain("get_project_info").And.Contain("get_planning"));
            Assert.That(prompt, Does.Contain("replyTo").And.Contain("狀態可能會變"));
            Assert.That(prompt, Does.Not.Contain("目前沒有對應的建議工具"));
            Assert.That(prompt, Does.Not.Contain("目前要在網頁上自己做"));
        }
    }

    [Test]
    public async Task LocalJob_OnOldAndNewKumi_GetsTheToolsAndThePromptThatMatch()
    {
        // Arrange
        await using var oldHarness = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        await using var newHarness = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var oldJob = await ClaimLocalAsync(oldHarness, AiRules.MinAgentVersionForFindItems);
        var newJob = await ClaimLocalAsync(newHarness, AiRules.MinAgentVersionForProjectWrites);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldJob.AllowedTools.Intersect(EightTools), Is.Empty);
            Assert.That(oldJob.SystemPrompt, Does.Not.Contain("- list_comments：").And.Contain("kumi update"));
            Assert.That(newJob.AllowedTools, Is.SupersetOf(EightTools));
            Assert.That(newJob.SystemPrompt, Does.Contain("- propose_project_changes：").And.Not.Contain("這一次沒有 list_comments"));
        }
    }

    [Test]
    public async Task HostJob_OnOldAndNewWorker_GetsTheToolsAndThePromptThatMatch()
    {
        // Arrange
        await using var oldHarness = await HostHarness.StartAsync();
        await using var newHarness = await HostHarness.StartAsync();
        var oldJob = await ClaimHostAsync(oldHarness, AiRules.MinAgentVersionForFindItems);
        var newJob = await ClaimHostAsync(newHarness, AiRules.MinAgentVersionForProjectWrites);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldJob.AllowedTools.Intersect(EightTools), Is.Empty);
            Assert.That(oldJob.SystemPrompt, Does.Not.Contain("- list_comments：").And.Contain("更新主機的 kumi"));
            Assert.That(newJob.AllowedTools, Is.SupersetOf(EightTools));
            Assert.That(newJob.SystemPrompt, Does.Contain("- propose_comment_changes：").And.Not.Contain("這一次沒有 list_comments"));
        }
    }

    private static async Task<LocalAgentJob> ClaimLocalAsync(LocalAgentHarness harness, string version)
    {
        harness.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();
        harness.AgentVersion = version;
        var engine = await harness.RegisterAsync();
        await harness.SendAsync(engine, "看一下留言");
        return await harness.ClaimAsync();
    }

    private static async Task<HostJob> ClaimHostAsync(HostHarness harness, string version)
    {
        var setup = await harness.SetUpAsync();
        setup.Worker.AgentVersion = version;
        await harness.CheckInAsync(setup.Worker);
        await harness.SendAsync(harness.Member, setup.Source, "看一下留言");
        return await harness.ClaimAsync(setup.Worker);
    }
}
