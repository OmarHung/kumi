using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Actions.SavedViews;

/// <summary>
/// 存的檢視（專案的，或「我的工作」的；<see cref="ActionContext.ProjectKey"/> 是 null 時是後者）的動作建議。
/// 檢視只有建立的人看得到，所以不需要額外的權限檢查：服務只列、只動自己的。套用一步就是一次服務呼叫，建立不冪等所以走通用的 applying。
/// </summary>
internal sealed class SavedViewActionHandler(IServiceProvider services) : IActionProposalHandler
{
    // 到用的時候才向容器要：沒有註冊檢視服務的主機（測試）也建得起所有的處理器。
    private ISavedViewService Views => services.GetRequiredService<ISavedViewService>();

    private static readonly JsonSerializerOptions StateOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string Kind => ActionKinds.SavedViews;

    public async Task<Result<PreparedAction>> PrepareAsync(ActionContext context, JsonElement payload, CancellationToken cancellationToken)
    {
        var isMyWork = context.ProjectKey is null;
        var existing = await ListAsync(context, cancellationToken);
        if (existing.Failure is { } failure)
        {
            return failure;
        }

        var steps = SavedViewPayload.Read(payload, isMyWork, existing.Value);
        if (steps.Failure is { } invalid)
        {
            return invalid;
        }

        var title = isMyWork ? "建議調整「我的工作」的檢視" : $"建議調整檢視（{context.ProjectKey}）";
        var link = isMyWork ? "/my-work" : $"/projects/{context.ProjectKey}";
        var cards = steps.Value.Select(step => ToCard(step)).ToList();
        var state = JsonSerializer.SerializeToElement(
            new SavedViewState(steps.Value.Select(s => new SavedViewStateStep(s.Action, s.ViewId, s.Draft)).ToList(), link), StateOptions);
        return new PreparedAction(title, cards, state, []);
    }

    public async Task<ActionStepResult> ApplyStepAsync(
        ActionContext context, JsonElement state, int index, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken)
    {
        var saved = state.Deserialize<SavedViewState>(StateOptions)!;
        var step = saved.Steps[index];
        var result = await RunAsync(context, step, cancellationToken);
        return result.Failure is { } failure ? ActionStepResult.Failed(failure) : ActionStepResult.Done(saved.Link);
    }

    private Task<Result<Unit>> RunAsync(ActionContext context, SavedViewStateStep step, CancellationToken cancellationToken) =>
        (context.ProjectKey, step.Action) switch
        {
            (null, SavedViewAction.Create) => Drop(Views.CreateMyWorkAsync(context.Actor, step.Draft!, cancellationToken)),
            (null, SavedViewAction.Update) => Drop(Views.ReplaceMyWorkAsync(context.Actor, step.ViewId!.Value, step.Draft!, cancellationToken)),
            (null, _) => Views.DeleteMyWorkAsync(context.Actor, step.ViewId!.Value, cancellationToken),
            ({ } key, SavedViewAction.Create) => Drop(Views.CreateAsync(context.Actor, key, step.Draft!, cancellationToken)),
            ({ } key, SavedViewAction.Update) => Drop(Views.ReplaceAsync(context.Actor, key, step.ViewId!.Value, step.Draft!, cancellationToken)),
            ({ } key, _) => Views.DeleteAsync(context.Actor, key, step.ViewId!.Value, cancellationToken),
        };

    private static async Task<Result<Unit>> Drop<T>(Task<Result<T>> task)
    {
        var result = await task;
        if (result.Failure is { } failure)
        {
            return failure;
        }

        return Unit.Value;
    }

    private async Task<Result<IReadOnlyList<SavedView>>> ListAsync(ActionContext context, CancellationToken cancellationToken)
    {
        if (context.ProjectKey is { } key)
        {
            return await Views.ListAsync(context.Actor, key, cancellationToken);
        }

        var mine = await Views.ListMyWorkAsync(context.Actor, cancellationToken);
        return mine.ToList();
    }

    private static ActionStep ToCard(SavedViewStep step)
    {
        var before = step.Previous is { } p ? SavedViewText.Describe(p.Name, p.Layout, p.Query) : null;
        var after = step.Draft is { } d ? SavedViewText.Describe(d.Name, d.Layout, d.Query) : null;
        var label = step.Action switch
        {
            SavedViewAction.Create => "新增檢視",
            SavedViewAction.Update => "修改檢視",
            _ => "刪除檢視",
        };
        return new ActionStep(label, step.Draft?.Name ?? step.Previous!.Name, before, after, null, ActionTextFormat.Text);
    }

    private sealed record SavedViewState(IReadOnlyList<SavedViewStateStep> Steps, string Link);

    private sealed record SavedViewStateStep(SavedViewAction Action, Guid? ViewId, SavedViewDraft? Draft);
}
