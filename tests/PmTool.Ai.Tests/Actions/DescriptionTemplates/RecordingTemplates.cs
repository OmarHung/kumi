using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.Actions.DescriptionTemplates;

/// <summary>說明格式服務的假物件：現有的清單由測試放，記下每次寫入的參數。其他方法這個測試用不到。</summary>
internal sealed class RecordingTemplates : IProjectDescriptionService
{
    public List<DescriptionTemplate> Templates { get; } =
        [new DescriptionTemplate("web_project", "網站案子", "## 基本資料", IsBuiltIn: true, IsDefault: true)];

    public List<string> Calls { get; } = [];

    public List<DescriptionTemplateInput> Inputs { get; } = [];

    public Failure? FailWith { get; set; }

    public Task<IReadOnlyList<DescriptionTemplate>> ListTemplatesAsync(Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DescriptionTemplate>>(Templates.ToList());

    public Task<Result<DescriptionTemplate>> CreateTemplateAsync(Actor actor, DescriptionTemplateInput input, CancellationToken cancellationToken) =>
        Record("create", null, input);

    public Task<Result<DescriptionTemplate>> UpdateTemplateAsync(Actor actor, Guid templateId, DescriptionTemplateInput input, CancellationToken cancellationToken) =>
        Record("update", templateId, input);

    public Task<Result<DescriptionTemplate>> DeleteTemplateAsync(Actor actor, Guid templateId, CancellationToken cancellationToken)
    {
        Calls.Add($"delete:{templateId}");
        return Task.FromResult(FailWith is { } failure ? (Result<DescriptionTemplate>)failure : Templates[0]);
    }

    public static DescriptionTemplate Own(string name, string body, bool isDefault = false) =>
        new(Guid.NewGuid().ToString(), name, body, IsBuiltIn: false, isDefault);

    private Task<Result<DescriptionTemplate>> Record(string call, Guid? id, DescriptionTemplateInput input)
    {
        Calls.Add(id is { } value ? $"{call}:{value}" : call);
        Inputs.Add(input);
        return Task.FromResult(
            FailWith is { } failure
                ? (Result<DescriptionTemplate>)failure
                : new DescriptionTemplate((id ?? Guid.NewGuid()).ToString(), input.Name, input.Body, IsBuiltIn: false, input.IsDefault));
    }

    public Task<Result<ProjectDescription>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<ProjectDescription>> SetAsync(Actor actor, string projectKey, string? text, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<ProjectSearchHit>>> SearchProjectsAsync(Actor actor, string text, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
