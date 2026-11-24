using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

internal sealed record ProposeCall(
    Actor Actor, string ProjectKey, string Document, DocumentFormat Format, ChangeSetOrigin Origin = ChangeSetOrigin.Direct);

internal sealed record ChangeSetCall(Actor Actor, string ProjectKey, long Number);

/// <summary>測試用的變更集服務：記住每次呼叫的參數（送出時包含收到的文件與格式），回傳值由測試設定。</summary>
internal sealed class FakeBlueprintChangeService : IBlueprintChangeService
{
    private readonly ConcurrentQueue<ProposeCall> _proposeCalls = new();
    private readonly ConcurrentQueue<ProposeCall> _previewCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey)> _listCalls = new();
    private readonly ConcurrentQueue<ChangeSetCall> _getCalls = new();
    private readonly ConcurrentQueue<ChangeSetCall> _applyCalls = new();
    private readonly ConcurrentQueue<ChangeSetCall> _rejectCalls = new();

    public Func<ProposeCall, Result<ChangeSetDetail>> OnPropose { get; set; } =
        call => TestData.ChangeSetDetail(TestData.ChangeSet(1, call.Actor.PersonId));

    public Func<ProposeCall, Result<ChangeSetPreview>> OnPreview { get; set; } =
        _ => new ChangeSetPreview(1, 1, TestData.Blueprint(), [], [], [], CanApply: true);

    public Func<Actor, string, Result<IReadOnlyList<ChangeSetSummary>>> OnList { get; set; } =
        (_, _) => Array.Empty<ChangeSetSummary>();

    public Func<ChangeSetCall, Result<ChangeSetDetail>> OnGet { get; set; } =
        call => TestData.ChangeSetDetail(TestData.ChangeSet(call.Number, call.Actor.PersonId));

    public Func<ChangeSetCall, Result<ChangeSetDetail>> OnApply { get; set; } =
        call => TestData.ChangeSetDetail(TestData.Decided(
            TestData.ChangeSet(call.Number, call.Actor.PersonId), ChangeSetStatus.Applied, call.Actor.PersonId));

    public Func<ChangeSetCall, Result<ChangeSetDetail>> OnReject { get; set; } =
        call => TestData.ChangeSetDetail(TestData.Decided(
            TestData.ChangeSet(call.Number, call.Actor.PersonId), ChangeSetStatus.Rejected, call.Actor.PersonId));

    public IReadOnlyList<ProposeCall> ProposeCalls => _proposeCalls.ToArray();

    public IReadOnlyList<ProposeCall> PreviewCalls => _previewCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey)> ListCalls => _listCalls.ToArray();

    public IReadOnlyList<ChangeSetCall> GetCalls => _getCalls.ToArray();

    public IReadOnlyList<ChangeSetCall> ApplyCalls => _applyCalls.ToArray();

    public IReadOnlyList<ChangeSetCall> RejectCalls => _rejectCalls.ToArray();

    /// <summary>六個方法加起來被呼叫了幾次，給「請求不該走到模組」的測試用。</summary>
    public int TotalCalls =>
        _proposeCalls.Count + _previewCalls.Count + _listCalls.Count + _getCalls.Count + _applyCalls.Count + _rejectCalls.Count;

    public Task<Result<ChangeSetDetail>> ProposeAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, CancellationToken cancellationToken) =>
        ProposeAsync(actor, projectKey, document, format, ChangeSetOrigin.Direct, cancellationToken);

    public Task<Result<ChangeSetDetail>> ProposeAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, ChangeSetOrigin origin, CancellationToken cancellationToken)
    {
        var call = new ProposeCall(actor, projectKey, document, format, origin);
        _proposeCalls.Enqueue(call);
        return Task.FromResult(OnPropose(call));
    }

    public Task<Result<ChangeSetPreview>> PreviewAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, CancellationToken cancellationToken)
    {
        var call = new ProposeCall(actor, projectKey, document, format);
        _previewCalls.Enqueue(call);
        return Task.FromResult(OnPreview(call));
    }

    public Task<Result<IReadOnlyList<ChangeSetSummary>>> ListAsync(
        Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        _listCalls.Enqueue((actor, projectKey));
        return Task.FromResult(OnList(actor, projectKey));
    }

    public Task<Result<ChangeSetDetail>> GetAsync(
        Actor actor, string projectKey, long number, CancellationToken cancellationToken) =>
        Respond(_getCalls, OnGet, new ChangeSetCall(actor, projectKey, number));

    public Task<Result<ChangeSetDetail>> ApplyAsync(
        Actor actor, string projectKey, long number, CancellationToken cancellationToken) =>
        Respond(_applyCalls, OnApply, new ChangeSetCall(actor, projectKey, number));

    public Task<Result<ChangeSetDetail>> RejectAsync(
        Actor actor, string projectKey, long number, CancellationToken cancellationToken) =>
        Respond(_rejectCalls, OnReject, new ChangeSetCall(actor, projectKey, number));

    private static Task<Result<ChangeSetDetail>> Respond(
        ConcurrentQueue<ChangeSetCall> calls, Func<ChangeSetCall, Result<ChangeSetDetail>> respond, ChangeSetCall call)
    {
        calls.Enqueue(call);
        return Task.FromResult(respond(call));
    }
}
