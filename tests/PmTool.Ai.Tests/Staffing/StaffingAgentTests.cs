using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.HostAgents;
using PmTool.Ai.Tests.LocalAgents;

namespace PmTool.Ai.Tests.Staffing;

/// <summary>
/// 派工的三個新工具只放給 kumi 0.16.0 以上：舊版的工作不放（它會整件工作不跑）、提示拿掉那幾行並請使用者更新；
/// 以及系統提示與本機、主機提示裡派工的內容。
/// </summary>
[TestFixture]
public sealed class StaffingAgentTests
{
    private static readonly string[] StaffingTools = ["get_project", "list_people", "propose_member_changes"];

    private const string NewVersion = AiRules.MinAgentVersionForStaffingTools;

    [TestCase("0.14.0", false, false)]
    [TestCase("0.15.0", true, false)]
    [TestCase(NewVersion, true, true)]
    public void ToolLists_FollowTheAgentVersion(string version, bool hasNewItems, bool hasStaffing)
    {
        // Act
        var tools = AgentToolLists.For("WEB", version);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Contains("propose_new_items"), Is.EqualTo(hasNewItems));
            Assert.That(StaffingTools.All(tools.Contains), Is.EqualTo(hasStaffing));
            Assert.That(StaffingTools.Any(tools.Contains), Is.EqualTo(hasStaffing));
        }
    }

    [Test]
    public void OrganizationChat_NeverGetsTheStaffingTools()
    {
        // Act
        var tools = AgentToolLists.For(null, NewVersion);

        // Assert
        Assert.That(StaffingTools.Any(tools.Contains), Is.False);
    }

    [Test]
    public void LocalPrompt_ForAKumiThatIsTooOldForStaffing_DropsTheLinesAndSaysToUpdate()
    {
        // Act
        var prompt = LocalPromptBuilder.SystemPrompt("WEB", [], supportsNewItems: true, supportsStaffing: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in StaffingTools)
            {
                Assert.That(prompt, Does.Not.Contain($"- {tool}："));
            }

            Assert.That(prompt, Does.Contain("- propose_new_items："));
            Assert.That(prompt, Does.Contain("這一次沒有 get_project、list_people、propose_member_changes 這三個工具").And.Contain("kumi update"));
            Assert.That(prompt, Does.Not.Contain("這一次沒有 propose_new_items"));
        }
    }

    [Test]
    public void HostPrompt_ForAWorkerThatIsTooOld_AsksTheOwnerToUpdate()
    {
        // Act
        var prompt = LocalPromptBuilder.HostSystemPrompt("WEB", [], supportsNewItems: false, supportsStaffing: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("這一次沒有 get_project、list_people、propose_member_changes 這三個工具").And.Contain("請組織擁有者更新主機的 kumi"));
            Assert.That(prompt, Does.Contain("這一次沒有 propose_new_items"));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void LocalAndHostPrompts_ForANewKumi_ListTheStaffingToolsAndTheNewParameters(bool host)
    {
        // Act
        var prompt = host ? LocalPromptBuilder.HostSystemPrompt("WEB", []) : LocalPromptBuilder.SystemPrompt("WEB", []);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in StaffingTools)
            {
                Assert.That(prompt, Does.Contain($"- {tool}："));
            }

            Assert.That(prompt, Does.Contain("addMembers").And.Contain("cycleId").And.Contain("milestoneId"));
            Assert.That(prompt, Does.Contain("jobTitle"));
            Assert.That(prompt, Does.Not.Contain("這一次沒有"));
        }
    }

    [Test]
    public void SystemPrompt_TeachesStaffingAndPermissionHonesty()
    {
        // Act
        var prompt = AssistantPrompt.SystemPrompt;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("代替使用者在這個專案做事"));
            Assert.That(prompt, Does.Contain("## 派工"));
            Assert.That(prompt, Does.Contain("openItems").And.Contain("memberManagers").And.Contain("addMembers").And.Contain("propose_member_changes"));
            Assert.That(prompt, Does.Contain("contributor").And.Contain("只看得到自己負責或建立的項目"));
            Assert.That(prompt, Does.Contain("不要說「AI 不能做」"));
            Assert.That(prompt, Does.Not.Contain("不在名單裡的人不要寫成提及"));
            Assert.That(prompt, Does.Contain("[REPLY]"));
        }
    }

    [TestCase("0.15.0", false)]
    [TestCase(NewVersion, true)]
    public async Task LocalJob_HasTheStaffingToolsOnlyForAKumiThatIsNewEnough(string version, bool hasTools)
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = version;
        h.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();
        var engine = await h.RegisterAsync();
        await h.SendAsync(engine, "這個任務該給誰");

        // Act
        var job = await h.ClaimAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(StaffingTools.All(job.AllowedTools.Contains), Is.EqualTo(hasTools));
            Assert.That(job.SystemPrompt.Contains("- get_project："), Is.EqualTo(hasTools));
            Assert.That(job.SystemPrompt.Contains("這一次沒有 get_project"), Is.EqualTo(!hasTools));
        }
    }

    [TestCase("0.15.0", false)]
    [TestCase(NewVersion, true)]
    public async Task HostJob_HasTheStaffingToolsOnlyForAWorkerThatIsNewEnough(string version, bool hasTools)
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        h.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();
        var setup = await h.SetUpAsync();
        setup.Worker.AgentVersion = version;
        await h.CheckInAsync(setup.Worker);
        await h.SendAsync(h.Member, setup.Source, "這個任務該給誰");

        // Act
        var job = await h.ClaimAsync(setup.Worker);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(StaffingTools.All(job.AllowedTools.Contains), Is.EqualTo(hasTools));
            Assert.That(job.SystemPrompt.Contains("- list_people："), Is.EqualTo(hasTools));
            Assert.That(job.SystemPrompt.Contains("這一次沒有 get_project") && job.SystemPrompt.Contains("請組織擁有者更新主機的 kumi"), Is.EqualTo(!hasTools));
        }
    }
}
