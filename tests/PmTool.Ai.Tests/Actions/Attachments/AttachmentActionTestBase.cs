using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Actions;
using PmTool.Ai.Actions.Attachments;
using PmTool.Ai.Persistence;
using PmTool.Ai.Tests.Actions.People;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.Attachments;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Actions.Attachments;

/// <summary>
/// 附件建議測試的共同環境：專案 WEB 的使用者有改項目的權限，項目 WEB-12 在裡面；知識庫與項目附件換成會記錄的假物件；
/// 使用者在對話 <see cref="ConversationId"/> 裡已經送出一個檔案「規格.pdf」。
/// </summary>
public abstract class AttachmentActionTestBase
{
    internal static readonly byte[] PdfBytes = Pdf(128);

    private IsolatedActions? _actions;

    internal AssistantHarness H { get; private set; } = null!;

    internal RecordingItemAttachments ItemFiles { get; private set; } = null!;

    internal FakePages Pages { get; private set; } = null!;

    internal RecordingPageAttachments PageFiles { get; private set; } = null!;

    internal Guid ConversationId { get; private set; }

    internal AssistantAttachment Spec { get; private set; } = null!;

    internal Actor Me => H.Actor;

    internal AttachmentActionHandler Handler => new(
        H.Host.Get<IDbContextFactory<AiDbContext>>(),
        H.Host.Get<PmTool.Storage.IFileStore>(),
        H.Host.Get<ILoggerFactory>(),
        H.WorkItems,
        H.Projects,
        ItemFiles,
        H.Host.Services);

    internal IsolatedActions Actions => _actions ??= new IsolatedActions(H, Handler);

    [SetUp]
    public async Task StartAsync()
    {
        _actions = null;
        ItemFiles = new RecordingItemAttachments();
        Pages = new FakePages();
        PageFiles = new RecordingPageAttachments();
        H = await AssistantHarness.StartAsync(configure: services =>
        {
            services.AddSingleton<PmTool.Knowledge.IKnowledgePageService>(Pages);
            services.AddSingleton<PmTool.Knowledge.IKnowledgeAttachmentService>(PageFiles);
        });
        UseProject(canUpdate: true);
        var conversation = await H.NewConversationAsync();
        ConversationId = conversation.Id;
        Spec = await SendFileAsync(conversation.Id, "規格.pdf", PdfBytes);
    }

    [TearDown]
    public async Task StopAsync() => await H.DisposeAsync();

    /// <summary>專案 WEB 的詳細資料：呼叫的人有沒有 item.update、有沒有封存。</summary>
    internal void UseProject(bool canUpdate, bool archived = false)
    {
        List<string> permissions = [.. WorkSamples.AllPermissions];
        if (canUpdate)
        {
            permissions.Add(PmTool.Work.WorkPermissions.ItemUpdate);
        }

        var detail = WorkSamples.Detail(permissions);
        H.Projects.OnGet = (_, _) => archived ? detail with { Project = detail.Project with { ArchivedAt = DateTimeOffset.UnixEpoch } } : detail;
    }

    /// <summary>上傳一個檔案並用它送出一則訊息（附件就綁在那則對話上）。</summary>
    internal async Task<AssistantAttachment> SendFileAsync(Guid conversationId, string fileName, byte[] content, Actor? actor = null)
    {
        var uploaded = await H.UploadAsync(fileName, content, actor);
        H.Script.Reply("看到了");
        var started = await H.TryStartAsync(conversationId, "請處理", [uploaded.Id], actor);
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        await AssistantHarness.ReadAllAsync(started.Value);
        return uploaded;
    }

    internal static JsonElement Payload(string changesJson)
    {
        using var document = JsonDocument.Parse($$"""{"changes":{{changesJson}}}""");
        return document.RootElement.Clone();
    }

    internal Task<Result<PreparedAction>> PrepareAsync(string changesJson, Actor? actor = null, string? projectKey = null, Guid? conversationId = null) =>
        Handler.PrepareAsync(new ActionContext(actor ?? Me, projectKey, conversationId ?? ConversationId), Payload(changesJson), CancellationToken.None);
}
