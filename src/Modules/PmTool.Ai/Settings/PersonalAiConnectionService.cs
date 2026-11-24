using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Settings;

/// <summary>
/// 成員自己的 AI 連線（internal/HANDOFF-ai-floating.md 第 4.1 節）。只看、只改擁有者是自己的連線：別人的、組織的一律「找不到」，
/// 不透露存不存在。新增、修改、重新讀取、讀取模型要組織允許（<see cref="AiSettings.AllowPersonalConnections"/>）；
/// 列出與刪除不看，組織關掉之後使用者還是看得到、刪得掉自己留下的連線。
/// </summary>
internal sealed class PersonalAiConnectionService(IDbContextFactory<AiDbContext> contexts, AiConnectionStore store) : IPersonalAiConnectionService
{
    private static readonly Failure Disabled =
        new(FailureKind.Forbidden, AiErrorCodes.PersonalDisabled, "組織目前不允許使用自己的 AI 帳號。");

    public Task<IReadOnlyList<AiConnection>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return store.ListAsync(ConnectionOwner.Personal(actor), cancellationToken);
    }

    public async Task<Result<AiConnection>> CreateAsync(Actor actor, AiConnectionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        return await IsAllowedAsync(actor, cancellationToken)
            ? await store.CreateAsync(actor, ConnectionOwner.Personal(actor), input, cancellationToken)
            : Disabled;
    }

    public async Task<Result<AiConnection>> UpdateAsync(Actor actor, Guid connectionId, AiConnectionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        return await IsAllowedAsync(actor, cancellationToken)
            ? await store.UpdateAsync(actor, ConnectionOwner.Personal(actor), connectionId, input, cancellationToken)
            : Disabled;
    }

    public async Task<Result<AiConnection>> RefreshModelsAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return await IsAllowedAsync(actor, cancellationToken)
            ? await store.RefreshAsync(actor, ConnectionOwner.Personal(actor), connectionId, cancellationToken)
            : Disabled;
    }

    public Task<Result<Unit>> DeleteAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return store.DeleteAsync(actor, ConnectionOwner.Personal(actor), connectionId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<AiDiscoveredModel>>> DiscoverModelsAsync(
        Actor actor, AiDiscoveryInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        return await IsAllowedAsync(actor, cancellationToken)
            ? await store.DiscoverModelsAsync(ConnectionOwner.Personal(actor), input, cancellationToken)
            : Disabled;
    }

    private Task<bool> IsAllowedAsync(Actor actor, CancellationToken cancellationToken) =>
        AiPersonalPolicy.IsAllowedAsync(contexts, actor.OrganizationId, cancellationToken);
}
