using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>
/// 日期與建議的現況每一輪都會變：放在最新一則使用者訊息前面的附註，不放在歷史之前的 system 訊息，
/// Anthropic 的自動快取才吃得到「系統提示加舊歷史」的前綴；附註也不能存進對話的 transcript。
/// </summary>
[TestFixture]
public sealed class AssistantTurnNoteTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private static string TextOf(ChatMessage message) => string.Concat(message.Contents.OfType<TextContent>().Select(t => t.Text));

    [Test]
    public async Task TwoTurns_TheSystemPrefixAndOldHistoryAreIdentical_AndTheNoteIsOnlyInFrontOfTheLastUserMessage()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("第一個回答").Reply("第二個回答");

        // Act
        await _h.SendAsync(conversation.Id, "第一個問題");
        _h.Clock.Advance(TimeSpan.FromMinutes(7));
        await _h.SendAsync(conversation.Id, "第二個問題");

        // Assert
        var first = _h.Script.Calls[0].Messages;
        var second = _h.Script.Calls[1].Messages;
        var secondUser = second[^1];
        var systemPrefix = second.TakeWhile(m => m.Role == ChatRole.System).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(systemPrefix, Has.Count.EqualTo(2), "只有系統提示與專案說明兩則 system 訊息");
            Assert.That(systemPrefix.Select(TextOf), Is.EqualTo(first.TakeWhile(m => m.Role == ChatRole.System).Select(TextOf)));
            Assert.That(systemPrefix.Select(TextOf), Has.None.Contain("現在是 20"));

            // 舊歷史（第一輪的使用者訊息）不帶附註，內容和第一輪送出的相同於附註以外的部分。
            var oldUser = second.Single(m => m.Role == ChatRole.User && TextOf(m).Contains("第一個問題"));
            Assert.That(TextOf(oldUser), Is.EqualTo("第一個問題"));
            Assert.That(secondUser.Role, Is.EqualTo(ChatRole.User));
            Assert.That(TextOf(secondUser), Does.StartWith(AssistantPrompt.TurnNotePrefix).And.Contain("現在是 2026-11-18").And.EndWith("第二個問題"));
            Assert.That(second.Count(m => TextOf(m).Contains(AssistantPrompt.TurnNotePrefix)), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task TheNote_IsNotStoredInTheTranscriptOrTheMessages()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        var reply = await _h.SendAsync(conversation.Id, "你好");
        var userMessage = (await _h.MessagesAsync(conversation.Id)).First(m => m.Role == AssistantRole.User);
        var row = await _h.RowAsync(reply.Final.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(userMessage.Parts.OfType<AssistantTextPart>().Single().Text, Is.EqualTo("你好"));
            Assert.That(row.Transcript, Does.Not.Contain("Kumi 附註"));
            Assert.That(row.Parts, Does.Not.Contain("Kumi 附註"));
        }
    }

    [Test]
    public async Task TheNote_IsSentToTheProviderEvenWhenThereIsNoHistory()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        await _h.SendAsync(conversation.Id, "你好");

        // Assert
        var last = _h.Script.Calls[0].Messages[^1];
        Assert.That(TextOf(last), Does.StartWith(AssistantPrompt.TurnNotePrefix).And.EndWith("你好"));
    }

    private sealed class BrokenClock : IOrganizationClock
    {
        private int _calls;

        /// <summary>送出訊息時用量的檢查會先讀兩次時區（月與日），第三次才是回覆開始時補日期的那一次。</summary>
        public int FailAtCall { get; set; }

        public Task<TimeZoneInfo> TimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) =>
            FailAtCall > 0 && Interlocked.Increment(ref _calls) == FailAtCall
                ? throw new InvalidOperationException("資料庫連不上。")
                : Task.FromResult(TimeZoneInfo.FindSystemTimeZoneById(OrganizationTime.DefaultTimeZoneId));
    }

    [Test]
    public async Task WhenTheTimeZoneCannotBeRead_TheReplyStillAnswersJustWithoutTheDate()
    {
        // Arrange
        var clock = new BrokenClock();
        await using var broken = await AssistantHarness.StartAsync(configure: services => services.AddSingleton<IOrganizationClock>(clock));
        var conversation = await broken.NewConversationAsync();
        clock.FailAtCall = 3;
        broken.Script.Reply("好");

        // Act
        var reply = await broken.SendAsync(conversation.Id, "你好");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(TextOf(broken.Script.Calls[0].Messages[^1]), Is.EqualTo("你好"));
        }
    }
}
