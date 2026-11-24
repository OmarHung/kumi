using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Live;
using PmTool.Api.Tests.Live;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 即時推送的端到端：真的工作引擎寫入、真的事件流、真的權限，用 SignalR 用戶端接收。
/// 讀事件與重新檢查連線都由測試直接跑一輪，不靠計時器。
/// 人物：O 是組織擁有者（看得到所有專案）、E 是專案的編輯者、C 是專案的執行者（只看自己的項目）、X 不是任何專案的成員、Y 用來測登出。
/// </summary>
[TestFixture]
public sealed class LiveFlowTests : LiveFlowTestBase
{
    private LiveUser _contributor = null!;
    private LiveUser _outsider = null!;
    private LiveUser _leaver = null!;

    /// <summary>登入有限流，整個類別只登入一次各人；每個測試開自己的專案。</summary>
    [OneTimeSetUp]
    public async Task SignInEveryoneAsync()
    {
        _contributor = await NewMemberAsync("contributor");
        _outsider = await NewMemberAsync("outsider");
        _leaver = await NewMemberAsync("leaver");
    }

    [OneTimeTearDown]
    public void DisposeUsers()
    {
        foreach (var user in new[] { _contributor, _outsider, _leaver })
        {
            user?.Dispose();
        }
    }

    [Test]
    public async Task Item_CreatedAndChanged_IsPushedToAMemberAsTheProjectKeyAndItemKeys()
    {
        // Arrange
        var project = await StartProjectAsync("LVA");
        await using var live = await ConnectAsync(Editor, [LiveGroups.Project(project)]);
        await Factory.RunLiveOnceAsync();

        // Act
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVA", new { type = "task", title = "新項目" });
        await Factory.RunLiveOnceAsync();
        var created = await live.NextAsync();
        await RealStackSteps.PatchItemAsync(Owner.Client, "LVA-1", new { title = "改過了" });
        await Factory.RunLiveOnceAsync();
        var updated = await live.NextAsync();

        // Assert
        Assert.That(created.Name, Is.EqualTo("items"));
        var expected = $$"""{"projectKey":"LVA","itemKeys":["LVA-1"],"actorId":"{{Owner.PersonId}}"}""";
        Assert.That(created.Payload.ToString(), Is.EqualTo(expected));
        Assert.That(updated.Payload.ToString(), Is.EqualTo(expected));
    }

    [Test]
    public async Task Item_Transitioned_Commented_AndDeleted_AreAllPushed()
    {
        // Arrange
        var project = await StartProjectAsync("LVB");
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVB", new { type = "task", title = "要被折騰的項目" });
        await using var live = await ConnectAsync(Editor, [LiveGroups.Project(project)]);
        await Factory.RunLiveOnceAsync();

        // Act
        await RealStackSteps.TransitionThroughAsync(Owner.Client, "LVB-1", "doing");
        await Factory.RunLiveOnceAsync();
        var transitioned = await live.NextAsync();
        await RealStackSteps.CommentAsync(Owner.Client, "LVB-1", "留言");
        await Factory.RunLiveOnceAsync();
        var commented = await live.NextAsync();
        using var deleted = await Owner.Client.DeleteAsync("/v1/items/LVB-1");
        await Factory.RunLiveOnceAsync();
        var gone = await live.NextAsync();

        // Assert
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(new[] { transitioned, commented, gone }.Select(m => m.Payload.GetProperty("itemKeys")[0].GetString()), Is.All.EqualTo("LVB-1"));
    }

    [Test]
    public async Task SeveralChangesInOneRound_AreMergedIntoOneMessageWithoutRepeatedKeys()
    {
        // Arrange
        var project = await StartProjectAsync("LVC");
        await using var live = await ConnectAsync(Editor, [LiveGroups.Project(project)]);
        await Factory.RunLiveOnceAsync();

        // Act
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVC", new { type = "task", title = "一" });
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVC", new { type = "task", title = "二" });
        await RealStackSteps.PatchItemAsync(Owner.Client, "LVC-1", new { title = "一改" });
        await Factory.RunLiveOnceAsync();
        var merged = await live.NextAsync();

        // Assert
        Assert.That(merged.Payload.GetProperty("itemKeys").EnumerateArray().Select(k => k.GetString()), Is.EqualTo(new[] { "LVC-1", "LVC-2" }));
        await live.ExpectNothingAsync();
    }

    [Test]
    public async Task MoreThanAHundredItemsInOneRound_AreSentAsNullMeaningTheWholeProject()
    {
        // Arrange
        var project = await StartProjectAsync("LVD");
        await using var live = await ConnectAsync(Editor, [LiveGroups.Project(project)]);
        await Factory.RunLiveOnceAsync();

        // Act
        for (var i = 1; i <= LiveEventRouter.MaxItemKeys + 1; i++)
        {
            await RealStackSteps.CreateItemAsync(Owner.Client, "LVD", new { type = "task", title = $"第 {i} 個" });
        }

        await Factory.RunLiveOnceAsync();
        var message = await live.NextAsync();

        // Assert
        Assert.That(message.Payload.GetProperty("projectKey").GetString(), Is.EqualTo("LVD"));
        Assert.That(message.Payload.GetProperty("itemKeys").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task Item_OfAProjectThePersonCannotRead_IsNeverPushedToThem()
    {
        // Arrange
        var project = await StartProjectAsync("LVE");
        await using var outsider = await ConnectAsync(_outsider);
        await using var editor = await ConnectAsync(Editor, [LiveGroups.Project(project)]);
        await Factory.RunLiveOnceAsync();

        // Act
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVE", new { type = "task", title = "機密" });
        await Factory.RunLiveOnceAsync();
        var received = await editor.NextAsync();

        // Assert
        Assert.That(received.Name, Is.EqualTo("items"));
        await outsider.ExpectNothingAsync();
    }

    [Test]
    public async Task OwnItemsOnlyMember_ReceivesOnlyItemsTheyAssignedCreatedOrUsedToBeAssignedTo()
    {
        // Arrange：C 只看得到自己負責或建立的項目。
        var project = await StartProjectAsync("LVF", (_contributor, "contributor"));
        await using var contributor = await ConnectAsync(_contributor, [LiveGroups.Own(project, _contributor.PersonId)]);
        await using var editor = await ConnectAsync(Editor, [LiveGroups.Project(project)]);
        await Factory.RunLiveOnceAsync();

        // Act：1 號指派給 C、2 號不是他的、3 號是他自己建立的。
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVF", new { type = "task", title = "給 C", assigneeId = _contributor.PersonId });
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVF", new { type = "task", title = "別人的" });
        await RealStackSteps.CreateItemAsync(_contributor.Client, "LVF", new { type = "task", title = "C 自己建立的" });
        await Factory.RunLiveOnceAsync();
        var first = await contributor.NextAsync();
        var everything = await editor.NextAsync();

        // 再把 1 號換給別人：C 是前一個負責人，還是會收到（他的畫面上那個項目要消失）。
        await RealStackSteps.PatchItemAsync(Owner.Client, "LVF-1", new { assigneeId = Editor.PersonId });
        await Factory.RunLiveOnceAsync();
        var handedOver = await contributor.NextAsync();

        // 之後 2 號有動靜，C 不該收到。
        await RealStackSteps.PatchItemAsync(Owner.Client, "LVF-2", new { title = "別人的，改過" });
        await Factory.RunLiveOnceAsync();

        // Assert
        Assert.That(Keys(first), Is.EqualTo(new[] { "LVF-1", "LVF-3" }));
        Assert.That(Keys(everything), Is.EqualTo(new[] { "LVF-1", "LVF-2", "LVF-3" }));
        Assert.That(Keys(handedOver), Is.EqualTo(new[] { "LVF-1" }));
        await contributor.ExpectNothingAsync();
    }

    [Test]
    public async Task NewNotification_IsPushedToItsRecipientAsInbox_AndNotToOthers()
    {
        // Arrange
        var project = await StartProjectAsync("LVG");
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVG", new { type = "task", title = "請 E 看一下" });
        await using var editor = await ConnectAsync(Editor, [LiveGroups.Project(project)]);
        await using var owner = await ConnectAsync(Owner, [LiveGroups.Project(project)]);
        await Factory.RunLiveOnceAsync();
        await Factory.RunNotificationsOnceAsync();
        await DrainAsync(editor, owner);

        // Act
        await RealStackSteps.CommentAsync(Owner.Client, "LVG-1", $"@[Editor](mention:{Editor.PersonId}) 麻煩看一下");
        await Factory.RunNotificationsOnceAsync();
        var inbox = await NextOfKindAsync(editor, "inbox");

        // Assert
        Assert.That(inbox.Payload.ToString(), Is.EqualTo("{}"));
        Assert.That((await owner.CollectAsync()).Select(m => m.Name), Does.Not.Contain("inbox"));
    }

    [Test]
    public async Task MarkingNotificationsRead_TellsTheOtherTabsOfThatPerson()
    {
        // Arrange
        var project = await StartProjectAsync("LVH");
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVH", new { type = "task", title = "請 E 看一下" });
        await RealStackSteps.CommentAsync(Owner.Client, "LVH-1", $"@[Editor](mention:{Editor.PersonId}) 麻煩看一下");
        await Factory.RunNotificationsOnceAsync();
        await using var otherTab = await ConnectAsync(Editor, [LiveGroups.Project(project)]);
        await Factory.RunLiveOnceAsync();

        // Act
        using var marked = await Editor.Client.PostAsync("/v1/notifications/read-all", content: null);
        var inbox = await NextOfKindAsync(otherTab, "inbox");

        // Assert
        Assert.That(marked.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(inbox.Payload.ToString(), Is.EqualTo("{}"));
    }

    [Test]
    public async Task MarkingAllReadWhenNothingIsUnread_DoesNotWakeTheOtherTabs()
    {
        // Arrange
        await using var otherTab = await ConnectAsync(_outsider);
        await Factory.RunLiveOnceAsync();

        // Act
        using var marked = await _outsider.Client.PostAsync("/v1/notifications/read-all", content: null);

        // Assert
        Assert.That(marked.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await otherTab.ExpectNothingAsync();
    }

    [Test]
    public async Task SignedOutSession_IsDisconnectedOnTheNextCheckAndOtherPeopleStayConnected()
    {
        // Arrange
        await using var leaver = await ConnectAsync(_leaver);
        await using var outsider = await ConnectAsync(_outsider);

        // Act
        using var signOut = await _leaver.Client.DeleteAsync(Sessions.Route);
        var abortedBefore = await Factory.RunLiveRefresherOnceAsync();
        await leaver.WaitUntilClosedAsync();

        // Assert
        Assert.That(signOut.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(abortedBefore, Is.EqualTo(1));
        Assert.That(outsider.Closed, Is.False);
        await WaitForNoConnectionsOfAsync(_leaver.PersonId);
    }

    [Test]
    public async Task MemberRemovedFromAProject_StopsReceivingItAfterTheNextCheck_AndAddedOnesStartToo()
    {
        // Arrange
        var removedFrom = await StartProjectAsync("LVI");
        var stays = await StartProjectAsync("LVJ");
        await using var editor = await ConnectAsync(Editor, [LiveGroups.Project(removedFrom), LiveGroups.Project(stays)]);
        await Factory.RunLiveOnceAsync();
        using var removed = await Owner.Client.DeleteAsync($"/v1/projects/LVI/members/{Editor.PersonId}");
        await Factory.RunLiveRefresherOnceAsync();
        await WaitForGroupsAsync(Editor.PersonId, [LiveGroups.Project(stays)]);
        Assert.That(
            Factory.Services.GetRequiredService<LiveConnections>().Snapshot().Single(c => c.PersonId == Editor.PersonId).Groups,
            Does.Not.Contain(LiveGroups.Project(removedFrom)));

        // Act：LVI 不再收到，LVJ 照常。
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVI", new { type = "task", title = "不該收到" });
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVJ", new { type = "task", title = "還是收到" });
        await Factory.RunLiveOnceAsync();
        var stillMember = await editor.NextAsync();

        var added = await StartProjectAsync("LVK");
        await Factory.RunLiveRefresherOnceAsync();
        await WaitForGroupsAsync(Editor.PersonId, [LiveGroups.Project(added)]);
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVK", new { type = "task", title = "新加入之後的" });
        await Factory.RunLiveOnceAsync();
        var justAdded = await editor.NextAsync();

        // Assert
        Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(stillMember.Payload.GetProperty("projectKey").GetString(), Is.EqualTo("LVJ"));
        Assert.That(justAdded.Payload.GetProperty("projectKey").GetString(), Is.EqualTo("LVK"));
        await editor.ExpectNothingAsync();
    }

    [Test]
    public async Task WebSocketConnection_ReceivesPushesToo()
    {
        // Arrange
        var project = await StartProjectAsync("LVL");
        await using var live = await ConnectAsync(Editor, [LiveGroups.Project(project)], HttpTransportType.WebSockets);
        await Factory.RunLiveOnceAsync();

        // Act
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVL", new { type = "task", title = "WebSocket" });
        await Factory.RunLiveOnceAsync();
        var message = await live.NextAsync();

        // Assert
        Assert.That(Keys(message), Is.EqualTo(new[] { "LVL-1" }));
    }

    [Test]
    public async Task ChangeRightAfterTheFirstConnectionIsRegistered_IsAlwaysPushed()
    {
        // Arrange：沒有任何連線，讀事件的工作照樣把位置跟到最新；連線登記之後馬上改項目，中間不再多跑一輪來「取起點」。
        var project = await StartProjectAsync("LVM");
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVM", new { type = "task", title = "任務" });
        await Factory.RunLiveOnceAsync();
        await using var live = await ConnectAsync(Editor, [LiveGroups.Project(project)], runBefore: false);

        // Act
        await RealStackSteps.PatchItemAsync(Owner.Client, "LVM-1", new { title = "連線之後馬上改" });
        await Factory.RunLiveOnceAsync();
        var message = await live.NextAsync();

        // Assert
        Assert.That(Keys(message), Is.EqualTo(new[] { "LVM-1" }));
    }

    [Test]
    public async Task ChangesMadeWhileNobodyWasConnected_AreNotReplayedToALaterConnection()
    {
        // Arrange：沒人連線時的事件由空轉的幾輪略過，之後才連上的人不該收到。
        var project = await StartProjectAsync("LVN");
        await RealStackSteps.CreateItemAsync(Owner.Client, "LVN", new { type = "task", title = "沒人在聽的時候" });
        Assert.That(await Factory.RunLiveOnceAsync(), Is.Zero, "沒有連線時不讀事件內容");
        await using var live = await ConnectAsync(Editor, [LiveGroups.Project(project)], runBefore: false);

        // Act
        var pushed = await Factory.RunLiveOnceAsync();

        // Assert
        Assert.That(pushed, Is.Zero);
        await live.ExpectNothingAsync();
    }

    private static async Task<(string Name, JsonElement Payload)> NextOfKindAsync(LiveHubClient client, string kind)
    {
        for (var i = 0; i < 10; i++)
        {
            var message = await client.NextAsync();
            if (message.Name == kind)
            {
                return message;
            }
        }

        throw new AssertionException($"沒有等到 {kind}");
    }

    private static async Task DrainAsync(params LiveHubClient[] clients)
    {
        foreach (var client in clients)
        {
            await client.CollectAsync();
        }
    }
}
