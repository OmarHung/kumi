using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的命令列瀏覽器登入：回測試設定的結果，並記下收到的參數。規則在身分模組自己的測試裡。</summary>
internal sealed class FakeCliLoginService : ICliLoginService
{
    private readonly ConcurrentQueue<(Actor Actor, CliLoginParameters Parameters)> _checks = new();
    private readonly ConcurrentQueue<(Actor Actor, CliLoginParameters Parameters, CliLoginGrant Grant)> _approvals = new();
    private readonly ConcurrentQueue<CliLoginRedemption> _redemptions = new();

    public Func<CliLoginParameters, Result<CliLoginRequest>> OnCheck { get; set; } =
        p => new CliLoginRequest(p.MachineName.Trim(), p.Tokens, p.Scopes);

    public Func<CliLoginParameters, CliLoginGrant, Result<CliLoginApproval>> OnApprove { get; set; } =
        (p, _) => new CliLoginApproval($"http://127.0.0.1:{p.Port}/kumi-login?code=the-code&state={p.State}");

    public Func<CliLoginRedemption, Result<CliLoginTokens>> OnRedeem { get; set; } = _ => new CliLoginTokens(
        new CliLoginAccount("Member Example", "member@example.com", "Example 組織"), null, null, false);

    public IReadOnlyList<(Actor Actor, CliLoginParameters Parameters)> Checks => _checks.ToArray();

    public IReadOnlyList<(Actor Actor, CliLoginParameters Parameters, CliLoginGrant Grant)> Approvals => _approvals.ToArray();

    public IReadOnlyList<CliLoginRedemption> Redemptions => _redemptions.ToArray();

    public Task<Result<CliLoginRequest>> CheckAsync(Actor actor, CliLoginParameters parameters, CancellationToken cancellationToken)
    {
        _checks.Enqueue((actor, parameters));
        return Task.FromResult(OnCheck(parameters));
    }

    public Task<Result<CliLoginApproval>> ApproveAsync(Actor actor, CliLoginParameters parameters, CliLoginGrant grant, CancellationToken cancellationToken)
    {
        _approvals.Enqueue((actor, parameters, grant));
        return Task.FromResult(OnApprove(parameters, grant));
    }

    public Task<Result<CliLoginTokens>> RedeemAsync(CliLoginRedemption redemption, CancellationToken cancellationToken)
    {
        _redemptions.Enqueue(redemption);
        return Task.FromResult(OnRedeem(redemption));
    }
}
