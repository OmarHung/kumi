using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant.Fakes;

/// <summary>專案說明的假物件：說明依專案代號放在 <see cref="Descriptions"/>；沒放的是空的。說明格式與搜尋用不到。</summary>
internal sealed class FakeProjectDescriptions : IProjectDescriptionService
{
    public Dictionary<string, ProjectDescription> Descriptions { get; } = new(StringComparer.Ordinal);

    public Failure? Failure { get; set; }

    public Task<Result<ProjectDescription>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<ProjectDescription>>(Failure is { } failure
            ? failure
            : Descriptions.GetValueOrDefault(projectKey, ProjectDescription.Empty));

    public Task<Result<ProjectDescription>> SetAsync(Actor actor, string projectKey, string? text, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            return Task.FromResult<Result<ProjectDescription>>(failure);
        }

        var saved = new ProjectDescription(string.IsNullOrWhiteSpace(text) ? null : text, DateTimeOffset.UtcNow, actor.PersonId);
        Descriptions[projectKey] = saved;
        return Task.FromResult<Result<ProjectDescription>>(saved);
    }

    public Task<IReadOnlyList<DescriptionTemplate>> ListTemplatesAsync(Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DescriptionTemplate>>([]);

    public Task<Result<DescriptionTemplate>> CreateTemplateAsync(Actor actor, DescriptionTemplateInput input, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<DescriptionTemplate>> UpdateTemplateAsync(
        Actor actor, Guid templateId, DescriptionTemplateInput input, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<DescriptionTemplate>> DeleteTemplateAsync(Actor actor, Guid templateId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<IReadOnlyList<ProjectSearchHit>>> SearchProjectsAsync(
        Actor actor, string text, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>項目附件的假物件：附件依項目編號放在 <see cref="ByItem"/>；只支援列出。</summary>
internal sealed class FakeAttachments : IAttachmentService
{
    public Dictionary<string, List<Attachment>> ByItem { get; } = new(StringComparer.Ordinal);

    public Task<Result<IReadOnlyList<Attachment>>> ListAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<Attachment>>>(ByItem.TryGetValue(itemKey, out var list) ? list.ToList() : []);

    public Task<Result<Attachment>> AddAsync(Actor actor, string itemKey, NewAttachment upload, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<AttachmentContent>> OpenAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Attachment>> DeleteAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
