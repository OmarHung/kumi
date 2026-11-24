using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// 自己的 AI 連線：成員接自己的供應商帳號（貼金鑰，或 <see cref="IOpenRouterLoginService"/> 登入），只有自己能用、費用算自己的帳號。
/// 組織管理員在 AI 設定開了 <see cref="AiSettings.AllowPersonalConnections"/> 才能新增、修改、讀取模型；關掉之後已經有的照樣列得出來、
/// 可以刪，但不會出現在能選的清單裡。規格見 <c>internal/HANDOFF-ai-floating.md</c> 第 3、4 節。
/// 欄位的規則與組織的連線相同（<see cref="IAiSettingsService"/>），只有名稱只在自己的連線之間不能重複。
/// 別人的、組織的連線一律 <see cref="AiErrorCodes.ConnectionNotFound"/>。
/// </summary>
public interface IPersonalAiConnectionService
{
    /// <summary>自己的連線，依名稱排序；每個帶這個月的用量。</summary>
    Task<IReadOnlyList<AiConnection>> ListAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>組織不允許時 <see cref="AiErrorCodes.PersonalDisabled"/>；超過 <see cref="AiRules.MaxPersonalConnections"/> 時細項 <see cref="AiErrorCodes.TooManyConnections"/>。</summary>
    Task<Result<AiConnection>> CreateAsync(Actor actor, AiConnectionInput input, CancellationToken cancellationToken);

    Task<Result<AiConnection>> UpdateAsync(Actor actor, Guid connectionId, AiConnectionInput input, CancellationToken cancellationToken);

    Task<Result<AiConnection>> RefreshModelsAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken);

    /// <summary>組織不允許時照樣可以刪。</summary>
    Task<Result<Unit>> DeleteAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken);

    /// <summary><see cref="AiDiscoveryInput.ConnectionId"/> 只能是自己的連線。</summary>
    Task<Result<IReadOnlyList<AiDiscoveredModel>>> DiscoverModelsAsync(
        Actor actor, AiDiscoveryInput input, CancellationToken cancellationToken);
}

/// <summary>連線屬於誰。JSON 是 <c>organization</c>、<c>personal</c>。</summary>
public enum AiConnectionScope
{
    /// <summary>組織的連線，只有組織管理員能接。</summary>
    Organization,

    /// <summary>自己的連線，組織允許時每個人都能接。</summary>
    Personal,
}

/// <summary>
/// 「用 OpenRouter 登入」：OAuth 加 PKCE（OpenRouter 的官方做法，不必事先註冊應用），登入後拿到一把綁那個 OpenRouter 帳號的金鑰，
/// 存成一個 <see cref="AiProviderKind.Openrouter"/> 的連線。規格見 <c>internal/HANDOFF-ai-floating.md</c> 第 4.2 節。
/// </summary>
public interface IOpenRouterLoginService
{
    /// <summary>
    /// 開始登入，回傳要把瀏覽器導去的 OpenRouter 授權網址（帶 <c>callback_url</c>、<c>code_challenge</c>、<c>state</c>）。
    /// <paramref name="scope"/> 是 <see cref="AiConnectionScope.Organization"/> 時要組織管理員（<see cref="AiErrorCodes.Forbidden"/>）；
    /// 是 <see cref="AiConnectionScope.Personal"/> 時組織要允許（<see cref="AiErrorCodes.PersonalDisabled"/>）。
    /// <paramref name="callbackUrl"/> 由 API 依這個部署的網址組（<c>{issuer}/ai/openrouter/callback</c>）。
    /// </summary>
    Task<Result<string>> StartAsync(Actor actor, AiConnectionScope scope, string callbackUrl, CancellationToken cancellationToken);

    /// <summary>
    /// 完成登入：檢查 <paramref name="state"/>（同一個人、還沒過期），用授權碼與 PKCE 的 verifier 換金鑰，讀模型清單，
    /// 新增一個連線（開放的模型先空著，使用者接著勾），回傳連線與它屬於誰。
    /// state 不對、過期、不是這個人的，或換不到金鑰：<see cref="AiErrorCodes.LoginFailed"/>，訊息說明原因。
    /// </summary>
    Task<Result<OpenRouterLoginResult>> CompleteAsync(Actor actor, string code, string state, CancellationToken cancellationToken);
}

public sealed record OpenRouterLoginResult(AiConnectionScope Scope, AiConnection Connection);
