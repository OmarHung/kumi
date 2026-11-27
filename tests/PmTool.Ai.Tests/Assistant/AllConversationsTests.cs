using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>
/// 跨專案的對話清單（浮動面板的「對話」選單）：自己在各專案與不選專案的對話，新的在前、帶專案代號；
/// 每個專案照開那個專案的對話時同一套權限過濾（看不到專案、沒有讀設定的權限、看過全部項目而現在只剩自己的），不另外一份規則。
/// </summary>
[TestFixture]
public sealed class AllConversationsTests
{
    private const string OtherKey = "APP";

    private static readonly Guid OtherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _h = await AssistantHarness.StartAsync();
        Serve(web: WorkSamples.AllPermissions, app: WorkSamples.AllPermissions);
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task ListAll_MyConversationsInEveryProjectAndWithoutAProject_NewestFirstWithTheirProjectKey()
    {
        // Arrange
        var inWeb = await _h.NewConversationAsync();
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        var inOrganization = await _h.NewOrganizationConversationAsync();
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        var inApp = await NewConversationInAppAsync();
        await _h.NewConversationAsync(_h.Admin);

        // Act
        var listed = (await _h.Service.ListAllAsync(_h.Actor, CancellationToken.None)).Value;

        // Assert
        Assert.That(
            listed.Select(l => (l.ProjectKey, l.Conversation.Id)),
            Is.EqualTo(new (string?, Guid)[] { (OtherKey, inApp.Id), (null, inOrganization.Id), (AssistantHarness.ProjectKey, inWeb.Id) }));
    }

    [Test]
    public async Task ListAll_AProjectINoLongerSee_IsLeftOut()
    {
        // Arrange
        await _h.NewConversationAsync();
        var inApp = await NewConversationInAppAsync();
        _h.Projects.OnList = _ => [Summary(OtherKey, OtherId)];

        // Act
        var listed = (await _h.Service.ListAllAsync(_h.Actor, CancellationToken.None)).Value;

        // Assert
        Assert.That(listed.Select(l => l.Conversation.Id), Is.EqualTo(new[] { inApp.Id }));
    }

    [Test]
    public async Task ListAll_ManyNewerConversationsInAProjectINoLongerSee_DoNotCrowdOutTheOnesISee()
    {
        // Arrange
        var inWeb = await _h.NewConversationAsync();
        for (var i = 0; i < AiRules.MaxConversationsListed; i++)
        {
            _h.Clock.Advance(TimeSpan.FromSeconds(1));
            await NewConversationInAppAsync();
        }

        _h.Projects.OnList = _ => [Summary(AssistantHarness.ProjectKey, WorkSamples.ProjectId)];

        // Act
        var listed = (await _h.Service.ListAllAsync(_h.Actor, CancellationToken.None)).Value;

        // Assert
        Assert.That(listed.Select(l => l.Conversation.Id).ToList(), Is.EqualTo(new[] { inWeb.Id }));
    }

    [Test]
    public async Task ListAll_AProjectWhereICannotReadTheSettings_IsLeftOut()
    {
        // Arrange
        await _h.NewConversationAsync();
        var inApp = await NewConversationInAppAsync();
        Serve(web: [WorkPermissions.ItemRead], app: WorkSamples.AllPermissions);

        // Act
        var listed = (await _h.Service.ListAllAsync(_h.Actor, CancellationToken.None)).Value;

        // Assert
        Assert.That(listed.Select(l => l.Conversation.Id), Is.EqualTo(new[] { inApp.Id }));
    }

    [Test]
    public async Task ListAll_SeenAllItemsButNowOnlyMyOwn_IsLeftOutAndDeletedLikeTheProjectList()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");
        await _h.SendAsync(conversation.Id);
        Serve(web: [WorkPermissions.ItemReadOwn, WorkPermissions.BlueprintRead], app: WorkSamples.AllPermissions);

        // Act
        var listed = (await _h.Service.ListAllAsync(_h.Actor, CancellationToken.None)).Value;
        var projectList = (await _h.Service.ListAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed, Is.Empty);
            Assert.That(projectList, Is.Empty);
        }
    }

    [Test]
    public async Task ListAll_AsksForEachProjectOnceNoMatterHowManyConversations()
    {
        // Arrange
        await _h.NewConversationAsync();
        await _h.NewConversationAsync();
        await NewConversationInAppAsync();
        var before = _h.Projects.Gets.Count;

        // Act
        await _h.Service.ListAllAsync(_h.Actor, CancellationToken.None);

        // Assert
        Assert.That(_h.Projects.Gets.Skip(before).Select(g => g.Key).Order(), Is.EqualTo(new[] { OtherKey, AssistantHarness.ProjectKey }));
    }

    [Test]
    public async Task ListAll_NoConversations_IsEmptyWithoutAskingAboutAnyProject()
    {
        // Act
        var listed = await _h.Service.ListAllAsync(_h.Actor, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Value, Is.Empty);
            Assert.That(_h.Projects.Gets, Is.Empty);
        }
    }

    /// <summary>兩個專案都看得到；各自的權限照參數。</summary>
    private void Serve(IReadOnlyList<string> web, IReadOnlyList<string> app)
    {
        _h.Projects.OnList = _ => [Summary(AssistantHarness.ProjectKey, WorkSamples.ProjectId), Summary(OtherKey, OtherId)];
        _h.Projects.OnGet = (_, key) => key == OtherKey
            ? WorkSamples.Detail(app) with { Project = Summary(OtherKey, OtherId) }
            : WorkSamples.Detail(web);
    }

    private static ProjectSummary Summary(string key, Guid id) => new(id, key, key + " 專案", 3, "admin", DateTimeOffset.UnixEpoch);

    private async Task<AssistantConversationSummary> NewConversationInAppAsync()
    {
        var created = await _h.Service.CreateAsync(_h.Actor, OtherKey, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value;
    }
}
