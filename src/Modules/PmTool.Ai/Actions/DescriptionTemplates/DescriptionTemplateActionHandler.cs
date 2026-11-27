using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Actions.DescriptionTemplates;

/// <summary>
/// 組織的專案說明格式（新增、修改、刪除）的動作建議。只有組織管理員能提：建立時先擋，套用時工作引擎再檢查一次。
/// 內建的格式不能動。內容是 Markdown，卡片用 Markdown 的差異顯示。
/// </summary>
internal sealed class DescriptionTemplateActionHandler(IServiceProvider services) : IActionProposalHandler
{
    // 到用的時候才向容器要：沒有註冊說明服務的主機（測試）也建得起所有的處理器。
    private IProjectDescriptionService Descriptions => services.GetRequiredService<IProjectDescriptionService>();

    private const string AdminOnly = "只有組織管理員可以管理說明格式；請請組織管理員提出，或請他在「說明格式」頁自己改。";

    private static readonly JsonSerializerOptions StateOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string Kind => ActionKinds.DescriptionTemplates;

    public async Task<Result<PreparedAction>> PrepareAsync(ActionContext context, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!context.Actor.IsOrganizationAdmin)
        {
            return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, AdminOnly);
        }

        var existing = await Descriptions.ListTemplatesAsync(context.Actor, cancellationToken);
        var steps = DescriptionTemplatePayload.Read(payload, existing);
        if (steps.Failure is { } invalid)
        {
            return invalid;
        }

        var state = JsonSerializer.SerializeToElement(
            new TemplateState(steps.Value.Select(ToStateStep).ToList()), StateOptions);
        return new PreparedAction("建議調整說明格式", steps.Value.Select(ToCard).ToList(), state, []);
    }

    public async Task<ActionStepResult> ApplyStepAsync(
        ActionContext context, JsonElement state, int index, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken)
    {
        var step = state.Deserialize<TemplateState>(StateOptions)!.Steps[index];
        var failure = step.Action switch
        {
            TemplateAction.Create => (await Descriptions.CreateTemplateAsync(context.Actor, step.Input!, cancellationToken)).Failure,
            TemplateAction.Update => await UpdateAsync(context, step, cancellationToken),
            _ => (await Descriptions.DeleteTemplateAsync(context.Actor, step.TemplateId!.Value, cancellationToken)).Failure,
        };
        return failure is null ? ActionStepResult.Done("/description-templates") : ActionStepResult.Failed(failure);
    }

    // 套用時讀現況、只疊上這份建議真的要改的欄位：建議提出之後別人改過的其他欄位不會被蓋回去；這一份已經被刪掉就是這一步失敗。
    private async Task<Failure?> UpdateAsync(ActionContext context, TemplateStateStep step, CancellationToken cancellationToken)
    {
        var current = (await Descriptions.ListTemplatesAsync(context.Actor, cancellationToken))
            .FirstOrDefault(t => !t.IsBuiltIn && string.Equals(t.Id, step.TemplateId!.Value.ToString(), StringComparison.OrdinalIgnoreCase));
        if (current is null)
        {
            return new Failure(FailureKind.NotFound, WorkErrorCodes.DescriptionTemplateNotFound, "這份說明格式已經被刪掉了，沒有修改。");
        }

        var changes = step.Changes!;
        var input = new DescriptionTemplateInput(changes.Name ?? current.Name, changes.Body ?? current.Body, changes.IsDefault ?? current.IsDefault);
        return (await Descriptions.UpdateTemplateAsync(context.Actor, step.TemplateId!.Value, input, cancellationToken)).Failure;
    }

    private static TemplateStateStep ToStateStep(TemplateStep step)
    {
        if (step.Action != TemplateAction.Update)
        {
            return new TemplateStateStep(step.Action, step.TemplateId, step.Input, null);
        }

        var (before, after) = (step.Previous!, step.Input!);
        var changes = new TemplateChanges(
            after.Name != before.Name ? after.Name : null,
            after.Body != before.Body ? after.Body : null,
            after.IsDefault != before.IsDefault ? after.IsDefault : null);
        return new TemplateStateStep(step.Action, step.TemplateId, null, changes);
    }

    private static ActionStep ToCard(TemplateStep step)
    {
        var label = step.Action switch
        {
            TemplateAction.Create => "新增說明格式",
            TemplateAction.Update => "修改說明格式",
            _ => "刪除說明格式",
        };
        var target = step.Previous is { } p && step.Input is { } i && p.Name != i.Name ? $"{p.Name} → {i.Name}" : step.Input?.Name ?? step.Previous!.Name;
        return new ActionStep(label, target, step.Previous?.Body, step.Input?.Body, Note(step), ActionTextFormat.Markdown);
    }

    private static string? Note(TemplateStep step)
    {
        var wasDefault = step.Previous?.IsDefault ?? false;
        var isDefault = step.Input?.IsDefault ?? false;
        if (step.Action == TemplateAction.Delete)
        {
            return wasDefault ? "這是組織的預設格式，刪掉之後預設回到內建的「網站案子」。已經寫好的專案說明不受影響。" : null;
        }

        return (wasDefault, isDefault) switch
        {
            (false, true) => "設為組織的預設格式。",
            (true, false) => "取消組織的預設格式，預設回到內建的「網站案子」。",
            _ => null,
        };
    }

    private sealed record TemplateState(IReadOnlyList<TemplateStateStep> Steps);

    private sealed record TemplateChanges(string? Name, string? Body, bool? IsDefault);

    private sealed record TemplateStateStep(TemplateAction Action, Guid? TemplateId, DescriptionTemplateInput? Input, TemplateChanges? Changes);
}
