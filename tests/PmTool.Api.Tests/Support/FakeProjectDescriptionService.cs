using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的專案說明服務：記住每次寫入的參數，回傳值由測試設定。</summary>
internal sealed class FakeProjectDescriptionService : IProjectDescriptionService
{
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, string? Text)> _setCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid? TemplateId, DescriptionTemplateInput Input)> _writeTemplateCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid TemplateId)> _deleteTemplateCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string Text, int Limit)> _searchCalls = new();

    public Func<Actor, string, Result<ProjectDescription>> OnGet { get; set; } = (_, _) => ProjectDescription.Empty;

    public Func<Actor, string, string?, Result<ProjectDescription>> OnSet { get; set; } =
        (actor, _, text) => new ProjectDescription(text, TestData.UpdatedAt, actor.PersonId);

    public IReadOnlyList<DescriptionTemplate> Templates { get; set; } =
        [new DescriptionTemplate("web_project", "網站案子", "## 基本資料\n- 客戶：", IsBuiltIn: true, IsDefault: true)];

    public Func<Actor, string, int, Result<IReadOnlyList<ProjectSearchHit>>> OnSearch { get; set; } =
        (_, _, _) => new List<ProjectSearchHit>();

    public IReadOnlyList<(Actor Actor, string Text, int Limit)> SearchCalls => _searchCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, string? Text)> SetCalls => _setCalls.ToArray();

    /// <summary>新增與修改共用：修改時 templateId 是那一份的編號，新增是 null。</summary>
    public Func<Actor, Guid?, DescriptionTemplateInput, Result<DescriptionTemplate>> OnWriteTemplate { get; set; } =
        (_, id, input) => new DescriptionTemplate(
            (id ?? Guid.Parse("00000000-0000-0000-0000-0000000000a1")).ToString(), input.Name, input.Body, IsBuiltIn: false, input.IsDefault);

    public Func<Actor, Guid, Result<DescriptionTemplate>> OnDeleteTemplate { get; set; } =
        (_, id) => new DescriptionTemplate(id.ToString(), "舊格式", "## 舊", IsBuiltIn: false, IsDefault: false);

    public IReadOnlyList<(Actor Actor, Guid? TemplateId, DescriptionTemplateInput Input)> WriteTemplateCalls => _writeTemplateCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid TemplateId)> DeleteTemplateCalls => _deleteTemplateCalls.ToArray();

    public Task<Result<ProjectDescription>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult(OnGet(actor, projectKey));

    public Task<Result<ProjectDescription>> SetAsync(Actor actor, string projectKey, string? text, CancellationToken cancellationToken)
    {
        _setCalls.Enqueue((actor, projectKey, text));
        return Task.FromResult(OnSet(actor, projectKey, text));
    }

    public Task<IReadOnlyList<DescriptionTemplate>> ListTemplatesAsync(Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult(Templates);

    public Task<Result<DescriptionTemplate>> CreateTemplateAsync(
        Actor actor, DescriptionTemplateInput input, CancellationToken cancellationToken)
    {
        _writeTemplateCalls.Enqueue((actor, null, input));
        return Task.FromResult(OnWriteTemplate(actor, null, input));
    }

    public Task<Result<DescriptionTemplate>> UpdateTemplateAsync(
        Actor actor, Guid templateId, DescriptionTemplateInput input, CancellationToken cancellationToken)
    {
        _writeTemplateCalls.Enqueue((actor, templateId, input));
        return Task.FromResult(OnWriteTemplate(actor, templateId, input));
    }

    public Task<Result<DescriptionTemplate>> DeleteTemplateAsync(Actor actor, Guid templateId, CancellationToken cancellationToken)
    {
        _deleteTemplateCalls.Enqueue((actor, templateId));
        return Task.FromResult(OnDeleteTemplate(actor, templateId));
    }

    public Task<Result<IReadOnlyList<ProjectSearchHit>>> SearchProjectsAsync(
        Actor actor, string text, int limit, CancellationToken cancellationToken)
    {
        _searchCalls.Enqueue((actor, text, limit));
        return Task.FromResult(OnSearch(actor, text, limit));
    }
}
