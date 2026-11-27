using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai.Actions;

namespace PmTool.Ai.Tests.Actions;

/// <summary>測試用的處理器：種類種類是 test（不和真的處理器撞）；每一步的結果由測試決定。</summary>
internal sealed class FakeActionHandler : IActionProposalHandler
{
    private readonly List<(int Index, IReadOnlyDictionary<string, string> Inputs, ActionContext Context)> _applied = [];

    public const string TestKind = "test";

    public string Kind { get; init; } = TestKind;

    public int StepCount { get; set; } = 3;

    public IReadOnlyList<ActionInput> Inputs { get; set; } = [];

    public Func<ActionContext, Result<Unit>> OnPrepare { get; set; } = _ => Unit.Value;

    public Func<int, ActionStepResult> OnStep { get; set; } = index => ActionStepResult.Done($"/knowledge/HR/{index + 1}");

    public IReadOnlyList<(int Index, IReadOnlyDictionary<string, string> Inputs, ActionContext Context)> Applied => _applied;

    public Task<Result<PreparedAction>> PrepareAsync(ActionContext context, JsonElement payload, CancellationToken cancellationToken)
    {
        if (OnPrepare(context).Failure is { } failure)
        {
            return Task.FromResult<Result<PreparedAction>>(failure);
        }

        var steps = Enumerable.Range(0, StepCount).Select(i => new ActionStep($"第 {i + 1} 步", "HR", "舊", "新", null, ActionTextFormat.Text)).ToList();
        var state = JsonSerializer.SerializeToElement(new { payload = payload.GetRawText(), count = StepCount });
        return Task.FromResult<Result<PreparedAction>>(new PreparedAction("建議調整", steps, state, Inputs));
    }

    public Task<ActionStepResult> ApplyStepAsync(
        ActionContext context, JsonElement state, int index, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken)
    {
        _applied.Add((index, inputs, context));
        return Task.FromResult(OnStep(index));
    }
}
