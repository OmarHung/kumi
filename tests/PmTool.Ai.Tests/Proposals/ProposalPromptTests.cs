using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Proposals;

namespace PmTool.Ai.Tests.Proposals;

/// <summary>「現在」那一句、建議現況那一則 system 訊息、舊版 kumi 的工具清單與提示（內建、本機、主機三條路）。</summary>
[TestFixture]
public sealed class ProposalPromptTests
{
    private static readonly string[] RevisionTools = ["withdraw_proposal", "propose_item_deletions", "propose_bulk_item_changes"];

    private static readonly TimeZoneInfo Taipei = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    private static CurrentTime At(int year, int month, int day, int hour, int minute, TimeZoneInfo? zone = null) =>
        new(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero), zone ?? Taipei);

    [Test]
    public void NowSentence_IsInTheOrganizationTimeZone_WithTheChineseWeekday()
    {
        // Arrange：UTC 的 11/26 23:51 在台北已經是 11/27（星期五）07:51。
        var now = At(2026, 11, 26, 23, 51);

        // Act
        var note = AssistantPrompt.TurnNote(now, []);

        // Assert
        Assert.That(note, Does.StartWith(AssistantPrompt.TurnNotePrefix).And.Contain("現在是 2026-11-27（星期五）07:51，時區 Asia/Taipei"));
    }

    [Test]
    public void NowSentence_FollowsAnotherTimeZone_AcrossTheDateLine()
    {
        // Arrange：同一個瞬間，紐約還是 11/26（星期四）18:51。
        var now = At(2026, 11, 26, 23, 51, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

        // Act
        var note = AssistantPrompt.TurnNote(now, []);

        // Assert
        Assert.That(note, Does.Contain("現在是 2026-11-26（星期四）18:51，時區 America/New_York"));
    }

    [Test]
    public void NowSentence_TellsTheModelNotToSuggestDatesInThePast()
    {
        // Act
        var note = AssistantPrompt.TurnNote(At(2026, 11, 27, 0, 0), []);

        // Assert
        Assert.That(note, Does.Contain("不要早於今天"));
    }

    [Test]
    public void Notes_ThatMustStayTheSameWithinAConversation_HaveNoDate()
    {
        // Assert：專案與組織的說明在同一則對話裡每一輪都要一樣，日期只在最新一則前面的附註。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AssistantPrompt.ProjectNote("WEB", []), Does.Not.Contain("現在是"));
            Assert.That(AssistantPrompt.OrganizationNote(true), Does.Not.Contain("現在是"));
            Assert.That(AssistantPrompt.TurnNote(null, []), Is.Empty);
        }
    }

    [Test]
    public void SystemPrompt_ThatIsCached_HasNoDate()
    {
        // Assert：系統提示要被快取，日期只寫在最新一則使用者訊息前面的附註。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AssistantPrompt.SystemPrompt, Does.Not.Contain("現在是 20"));
            Assert.That(AssistantPrompt.OrganizationSystemPrompt, Does.Not.Contain("現在是 20"));
        }
    }

    [Test]
    public void ProposalNote_WithNoProposals_IsEmpty() =>
        Assert.That(AssistantPrompt.ProposalNote([]), Is.Empty);

    [Test]
    public void ProposalNote_ListsReferenceKindStateAndSummary_WithTheDecisionTime()
    {
        // Arrange
        var now = At(2026, 11, 27, 1, 0);
        var applied = new ProposalStatusEntry(
            "4f0c3b7e-0000-4000-8000-000000000001", ProposalKind.NewItems, ProposalState.Applied, "新增 7 個項目，已建立 WEB-38～WEB-44",
            new DateTimeOffset(2026, 11, 26, 23, 40, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 26, 23, 51, 0, TimeSpan.Zero));
        var pending = new ProposalStatusEntry(
            "changeset:12", ProposalKind.ChangeSet, ProposalState.Pending, "變更集 #12", new DateTimeOffset(2026, 11, 26, 22, 0, 0, TimeSpan.Zero), null);

        // Act
        var note = AssistantPrompt.ProposalNote([applied, pending], now);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(note, Does.Contain("以這裡為準").And.Contain("使用者可能已經在網頁上按過"));
            Assert.That(note, Does.Contain("- 4f0c3b7e-0000-4000-8000-000000000001｜新增項目｜已套用（07:51）｜新增 7 個項目，已建立 WEB-38～WEB-44"));
            Assert.That(note, Does.Contain("- changeset:12｜變更集｜等待中（使用者還沒按）（提出於 06:00）｜變更集 #12"));
        }
    }

    [Test]
    public void KeyLists_CollapseRunsOfThreeOrMore_AndKeepSeparateOnesApart()
    {
        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(KeyLists.Describe(["WEB-38", "WEB-39", "WEB-40", "WEB-41"]), Is.EqualTo("WEB-38～WEB-41"));
            Assert.That(KeyLists.Describe(["WEB-1", "WEB-2"]), Is.EqualTo("WEB-1、WEB-2"));
            Assert.That(KeyLists.Describe(["WEB-1", "WEB-3", "OPS-4", "OPS-5", "OPS-6"]), Is.EqualTo("WEB-1、WEB-3、OPS-4～OPS-6"));
        }
    }

    [Test]
    public void KeyLists_WithManyKeys_ShowsTheFirstGroupsAndTheTotal()
    {
        // Arrange
        var keys = Enumerable.Range(1, 30).Select(i => $"WEB-{i * 2}").ToList();

        // Act
        var text = KeyLists.Describe(keys);

        // Assert
        Assert.That(text, Does.EndWith("…（共 30 個）"));
    }

    [Test]
    public void TurnNote_HasTheDateAndTheProposals_AndSaysItIsNotFromTheUser()
    {
        // Arrange
        var entry = new ProposalStatusEntry("id-1", ProposalKind.ItemDeletions, ProposalState.Pending, "刪除 1 個項目：WEB-5", DateTimeOffset.UnixEpoch, null);

        // Act
        var note = AssistantPrompt.TurnNote(At(2026, 11, 27, 0, 0), [entry]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(note, Does.StartWith("〔Kumi 附註，不是使用者寫的〕"));
            Assert.That(note, Does.Contain("現在是 2026-11-27（星期五）08:00").And.Contain("id-1｜刪除項目｜等待中"));
            Assert.That(note.IndexOf("現在是", StringComparison.Ordinal), Is.LessThan(note.IndexOf("id-1｜", StringComparison.Ordinal)));
        }
    }

    [Test]
    public void EnginePrompt_PutsTheTurnNoteInFrontOfTheLastMessage_AndKeepsTheSystemPromptStable()
    {
        // Arrange
        var entry = new ProposalStatusEntry("id-1", ProposalKind.ItemDeletions, ProposalState.Pending, "刪除 1 個項目：WEB-5", DateTimeOffset.UnixEpoch, null);
        var first = AssistantPrompt.TurnNote(At(2026, 11, 27, 0, 0), [entry]);
        var second = AssistantPrompt.TurnNote(At(2026, 11, 27, 0, 5), [entry]);

        // Act
        var promptOne = LocalPromptBuilder.Prompt([], "嗨", null, first);
        var promptTwo = LocalPromptBuilder.Prompt([], "嗨", null, second);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(promptOne, Does.Contain("現在是 2026-11-27（星期五）08:00").And.Contain("id-1｜刪除項目｜等待中"));
            Assert.That(promptOne.IndexOf("id-1｜", StringComparison.Ordinal), Is.LessThan(promptOne.LastIndexOf("<<<使用者>>>", StringComparison.Ordinal)));
            Assert.That(promptTwo, Does.Contain("08:05"));
            Assert.That(LocalPromptBuilder.SystemPrompt("WEB", []), Does.Not.Contain("現在是 20").And.Not.Contain("id-1"));
        }
    }

    [Test]
    public void TurnNote_ForAKumiWithoutWithdraw_DoesNotTellTheModelToUseIt()
    {
        // Arrange
        var entry = new ProposalStatusEntry("id-1", ProposalKind.NewItems, ProposalState.Pending, "新增 1 個項目", DateTimeOffset.UnixEpoch, null);

        // Act
        var old = AssistantPrompt.TurnNote(null, [entry], supportsRevision: false);
        var current = AssistantPrompt.TurnNote(null, [entry], supportsRevision: true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(old, Does.Not.Contain("withdraw_proposal").And.Contain("id-1｜"));
            Assert.That(current, Does.Contain("withdraw_proposal"));
        }
    }

    [Test]
    public void Summaries_DoNotShowCustomFieldKeys()
    {
        // Arrange
        var changes = new[] { new ItemFieldChange("fields.secret_name", System.Text.Json.JsonSerializer.SerializeToElement("x")), new ItemFieldChange("dueDate", System.Text.Json.JsonSerializer.SerializeToElement("2026-12-01")) };
        var proposal = new ItemProposal(Guid.NewGuid(), "WEB", "WEB-1", "標題", changes, null, ItemProposalStatus.Pending, DateTimeOffset.UnixEpoch);

        // Act
        var entry = ProposalSummaries.Of(proposal);

        // Assert
        Assert.That(entry.Summary, Does.Contain("自訂欄位").And.Contain("dueDate").And.Not.Contain("secret_name"));
    }

    [TestCase(true, "kumi update")]
    [TestCase(false, "請組織擁有者更新主機的 kumi")]
    public void EnginePrompt_ForAKumiTooOldForRevision_DropsTheThreeToolsAndSaysToUpdate(bool local, string advice)
    {
        // Act
        var prompt = local
            ? LocalPromptBuilder.SystemPrompt("WEB", [], supportsRevision: false)
            : LocalPromptBuilder.HostSystemPrompt("WEB", [], supportsRevision: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in RevisionTools)
            {
                Assert.That(prompt, Does.Not.Contain($"- {tool}："));
            }

            Assert.That(prompt, Does.Contain("- propose_member_changes：").And.Contain("- propose_new_items："));
            Assert.That(prompt, Does.Contain("這一次沒有 withdraw_proposal、propose_item_deletions、propose_bulk_item_changes 這 3 個工具").And.Contain(advice));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void EnginePrompt_ForANewKumi_ListsTheThreeToolsAndTheProposalRules(bool local)
    {
        // Act
        var prompt = local ? LocalPromptBuilder.SystemPrompt("WEB", []) : LocalPromptBuilder.HostSystemPrompt("WEB", []);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in RevisionTools)
            {
                Assert.That(prompt, Does.Contain($"- {tool}："));
            }

            Assert.That(prompt, Does.Contain("已經提出的建議").And.Contain("以那裡為準"));
            Assert.That(prompt, Does.Not.Contain("這一次沒有"));
        }
    }

    [Test]
    public void SystemPrompt_NoLongerSaysDeletingHasToBeDoneByHand()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AssistantPrompt.SystemPrompt, Does.Not.Contain("（刪除項目、建立週期與里程碑這類）"));
            Assert.That(AssistantPrompt.SystemPrompt, Does.Contain("propose_item_deletions").And.Contain("propose_bulk_item_changes").And.Contain("withdraw_proposal"));
            Assert.That(AssistantPrompt.SystemPrompt, Does.Contain("已經套用（建立）了：改建出來的項目").And.Contain("不要再提一份新增項目建議"));
            Assert.That(AssistantPrompt.SystemPrompt, Does.Contain("不能早於今天"));
        }
    }

    [Test]
    public void AgentToolLists_OnlyGiveTheRevisionToolsToAKumiThatKnowsThem()
    {
        // Act
        var old = AgentToolLists.For("WEB", "0.16.0");
        var current = AgentToolLists.For("WEB", AiRules.MinAgentVersionForRevisionTools);
        var oldOrganization = AgentToolLists.For(null, "0.16.0");
        var currentOrganization = AgentToolLists.For(null, AiRules.MinAgentVersionForRevisionTools);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RevisionTools.Any(old.Contains), Is.False);
            Assert.That(old, Does.Contain("get_project"));
            Assert.That(RevisionTools.All(current.Contains), Is.True);
            Assert.That(oldOrganization, Does.Not.Contain("withdraw_proposal"));
            Assert.That(currentOrganization, Does.Contain("withdraw_proposal"));
            Assert.That(currentOrganization, Does.Not.Contain("propose_item_deletions"));
        }
    }
}
