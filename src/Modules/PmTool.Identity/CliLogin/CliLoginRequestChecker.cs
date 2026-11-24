using PmTool.Abstractions;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.OAuth;

namespace PmTool.Identity.CliLogin;

/// <summary>瀏覽器登入請求的欄位檢查。一次列出每個欄位的問題；細項的路徑用 HTTP 內文的屬性名稱，網頁才標得到欄位。</summary>
internal static class CliLoginRequestChecker
{
    public const string PortPath = "port";

    public const string StatePath = "state";

    public const string ChallengePath = "challenge";

    public const string MachinePath = "machine";

    public const string TokensPath = "tokens";

    public const string ScopesPath = "scopes";

    public static Result<CliLoginRequest> Check(CliLoginParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var machine = (parameters.MachineName ?? string.Empty).Trim();
        var wantsCli = parameters.Tokens?.Contains(CliLoginTokenKind.Cli) == true;
        FailureDetail?[] found =
        [
            CheckPort(parameters.Port),
            CheckState(parameters.State),
            Invalid(ChallengePath, "PKCE 的 challenge 要是 43 到 128 個 base64url 字元。", !Pkce.IsValidChallenge(parameters.CodeChallenge)),
            CheckMachine(machine),
            CheckTokens(parameters.Tokens),
            CheckScopes(parameters.Scopes, wantsCli),
        ];
        IReadOnlyList<FailureDetail> issues = [.. found.OfType<FailureDetail>()];
        return issues.Count == 0
            ? new CliLoginRequest(machine, parameters.Tokens!, parameters.Scopes)
            : new Failure(FailureKind.Invalid, issues[0].Code, issues[0].Message) { Details = issues };
    }

    private static FailureDetail? Invalid(string path, string message, bool isInvalid) =>
        isInvalid ? new(IdentityErrorCodes.CliLoginRequestInvalid, path, message) : null;

    private static FailureDetail? CheckPort(int port) =>
        Invalid(PortPath, $"連接埠要在 {CliLoginRules.MinPort} 到 {CliLoginRules.MaxPort} 之間。",
            port is < CliLoginRules.MinPort or > CliLoginRules.MaxPort);

    private static FailureDetail? CheckState(string? state) =>
        Invalid(
            StatePath,
            $"state 要是 {CliLoginRules.MinStateLength} 到 {CliLoginRules.MaxStateLength} 個 base64url 字元。",
            state is not { Length: >= CliLoginRules.MinStateLength and <= CliLoginRules.MaxStateLength } || !state.All(IsBase64UrlCharacter));

    private static bool IsBase64UrlCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';

    private static FailureDetail? CheckMachine(string machine)
    {
        if (machine.Length is 0 or > CliLoginRules.MaxMachineNameLength)
        {
            return new(IdentityErrorCodes.CliLoginRequestInvalid, MachinePath, $"電腦名稱要是 1 到 {CliLoginRules.MaxMachineNameLength} 個字。");
        }

        return InputText.HasForbiddenCharacters(machine) || AccessTokenInput.HasInvisibleFormatting(machine)
            ? new(IdentityErrorCodes.CliLoginRequestInvalid, MachinePath, "電腦名稱不能有換行、控制字元或無效的字元。")
            : null;
    }

    private static FailureDetail? CheckTokens(IReadOnlyList<CliLoginTokenKind>? tokens) =>
        Invalid(
            TokensPath,
            "要的權杖至少一種，不能重複，也不能有不認得的種類。",
            tokens is not { Count: > 0 } || tokens.Distinct().Count() != tokens.Count || tokens.Any(kind => !Enum.IsDefined(kind)));

    private static FailureDetail? CheckScopes(IReadOnlyList<AccessTokenScope>? scopes, bool wantsCli)
    {
        scopes ??= [];
        return CheckScopeList(scopes, wantsCli) is { } message ? new(IdentityErrorCodes.CliLoginRequestInvalid, ScopesPath, message) : null;
    }

    /// <summary>命令列用的範圍清單：要 cli 時至少一個、不重複、不能有 agent:run；不要 cli 時一定是空的。</summary>
    public static string? CheckScopeList(IReadOnlyList<AccessTokenScope> scopes, bool wantsCli)
    {
        if (!wantsCli)
        {
            return scopes.Count == 0 ? null : "沒有要命令列的權杖時，不能有範圍。";
        }

        if (scopes.Count == 0)
        {
            return "至少要選一個範圍。";
        }

        return scopes.Distinct().Count() != scopes.Count || scopes.Any(scope => !Enum.IsDefined(scope) || scope == AccessTokenScope.AgentRun)
            ? "範圍不能重複，也不能有不認得的範圍或 agent:run。"
            : null;
    }
}
