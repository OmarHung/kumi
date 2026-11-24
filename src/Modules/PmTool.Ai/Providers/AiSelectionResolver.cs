using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.HostAgents;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;
using PmTool.Ai.Settings;

namespace PmTool.Ai.Providers;

/// <summary>選好、檢查過的連線與模型，金鑰已經解開。</summary>
internal sealed record ResolvedSelection(AiSelection Selection, AiEndpoint Endpoint, string ConnectionName, AiModel Model);

/// <summary>
/// 檢查成員選的連線、模型、思考深度現在能不能用（internal/HANDOFF-ai-assistant.md 第 4.1 節）。工作包 A1 實作，A2 的助理用它。
/// </summary>
internal sealed class AiSelectionResolver(
    IDbContextFactory<AiDbContext> contexts, AiApiKeyProtector protector, TimeProvider time, HostSourceResolver hostSources)
{
    /// <summary>電腦離線時的說明；網頁直接顯示。指令名稱和網頁的 <c>COMMAND_NAME</c> 同一個字。</summary>
    public const string AgentOfflineMessage = "你電腦上的 Kumi 代理目前沒有連線，請執行 kumi agent start。";

    /// <summary>
    /// 組織的連線：組織沒開 AI 時 <see cref="AiErrorCodes.Unavailable"/>。自己的連線：組織要允許個人連線，不然（和別人的個人連線、
    /// 不存在的連線一樣）<see cref="AiErrorCodes.SelectionInvalid"/>，別人的不透露它存不存在。
    /// 模型沒開放、思考深度不在那個模型的清單裡（null 一律可以）、存的金鑰解不開：<see cref="AiErrorCodes.SelectionInvalid"/>，訊息說哪一個不能用。
    /// </summary>
    public async Task<Result<ResolvedSelection>> ResolveAsync(Actor actor, AiSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(selection);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == actor.OrganizationId, cancellationToken);
        var row = await db.Connections.AsNoTracking().SingleOrDefaultAsync(
            c => c.OrganizationId == actor.OrganizationId && c.Id == selection.ConnectionId, cancellationToken);
        if (row is null && await LocalEngineLookup.FindAsync(db, actor, selection.ConnectionId, cancellationToken) is { } local)
        {
            return ResolveLocal(settings, local, selection);
        }

        // 組織擁有者開的主機來源：成員（擁有者、管理員、一般成員）都能用，和組織的 AI 開關、個人連線的開關無關。
        if (row is null && await hostSources.TryResolveAsync(actor, selection, cancellationToken) is { } hosted)
        {
            return hosted;
        }

        if (CheckAccess(actor, settings, row) is { } denied)
        {
            return denied;
        }

        var model = AiModelsJson.Deserialize(row!.Models).FirstOrDefault(m => m.Id == selection.Model);
        if (model is not { Enabled: true })
        {
            return Invalid($"模型「{selection.Model}」現在沒有開放，請換一個。");
        }

        if (selection.Effort is { } effort && !model.Efforts.Contains(effort))
        {
            return Invalid($"模型「{model.DisplayName}」沒有「{AiEnumNames.NameOf(effort)}」這個思考深度，請換一個。");
        }

        string? key = null;
        if (row.ProtectedApiKey is { } stored)
        {
            if (!protector.TryUnprotect(stored, out var plain))
            {
                return Invalid(row.OwnerPersonId is null ? "存的金鑰解不開，請管理員重新輸入。" : "存的金鑰解不開，請到「我的帳號」重新輸入。");
            }

            key = plain;
        }

        return new ResolvedSelection(selection, new AiEndpoint(AiEnumNames.ParseKind(row.Kind), row.BaseUrl, key, row.OwnerPersonId is not null), row.Name, model);
    }

    /// <summary>
    /// 自己電腦上的引擎：組織要允許個人連線、那台電腦在線（<see cref="AiRules.LocalAgentOnlineWindow"/> 內登記過）、
    /// 模型在引擎回報的清單裡、思考深度在那個模型的清單裡。別人的引擎在查的時候就已經找不到。
    /// </summary>
    private Result<ResolvedSelection> ResolveLocal(AiSettingsRow? settings, LocalEngine local, AiSelection selection)
    {
        if (settings is not { AllowPersonalConnections: true })
        {
            return Invalid("組織目前不允許使用自己的 AI 帳號，請換一個。");
        }

        if (!LocalAgentDirectory.IsOnline(local.Agent.LastSeenAt, time.GetUtcNow()))
        {
            return Invalid(AgentOfflineMessage);
        }

        var model = LocalModelsJson.Deserialize(local.Engine.Models).FirstOrDefault(m => m.Id == selection.Model);
        if (model is null)
        {
            return Invalid($"模型「{selection.Model}」不在這台電腦回報的清單裡，請換一個。");
        }

        if (selection.Effort is { } effort && !model.Efforts.Contains(effort))
        {
            return Invalid($"模型「{model.DisplayName}」沒有「{AiEnumNames.NameOf(effort)}」這個思考深度，請換一個。");
        }

        var kind = AiEnumNames.ParseKind(local.Engine.Kind);
        return new ResolvedSelection(
            selection,
            new AiEndpoint(kind, null, null, Personal: true),
            LocalEngineLookup.NameOf(kind, local.Agent.Name),
            new AiModel(model.Id, model.DisplayName, model.Efforts, Enabled: true, Custom: false));
    }

    // 組織的連線要組織開著；自己的連線要組織允許，而且擁有者是自己。組織關掉 AI 時自己的連線照樣能用。
    private static Failure? CheckAccess(Actor actor, AiSettingsRow? settings, AiConnectionRow? row)
    {
        if (row is null)
        {
            return settings is { Enabled: true } or { AllowPersonalConnections: true }
                ? Invalid("選的連線已經不存在，請換一個。")
                : new Failure(FailureKind.Invalid, AiErrorCodes.Unavailable, "組織沒有開啟 AI 助理。");
        }

        if (row.OwnerPersonId is null)
        {
            return settings is { Enabled: true }
                ? null
                : new Failure(FailureKind.Invalid, AiErrorCodes.Unavailable, "組織沒有開啟 AI 助理。");
        }

        if (row.OwnerPersonId != actor.PersonId)
        {
            return Invalid("選的連線已經不存在，請換一個。");
        }

        return settings is { AllowPersonalConnections: true } ? null : Invalid("組織目前不允許使用自己的 AI 帳號，請換一個。");
    }

    private static Failure Invalid(string message) => new(FailureKind.Invalid, AiErrorCodes.SelectionInvalid, message);
}
