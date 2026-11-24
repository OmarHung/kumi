using PmTool.Cli.Commands;

namespace PmTool.Cli.Settings;

internal enum TokenSource
{
    Environment,
    ConfigFile,
}

internal sealed record Credentials(string Server, string Token, TokenSource Source)
{
    public const string ServerVariable = "KUMI_SERVER";
    public const string TokenVariable = "KUMI_TOKEN";
    public const string AllowInsecureVariable = "KUMI_ALLOW_INSECURE";
    public const string TokenPrefix = "pmt_";

    // 改名 Kumi 之前的名稱：已經設好的 CI 與指令稿升級之後照樣能用。新舊都有時用新的。
    private const string LegacyServerVariable = "PM_SERVER";
    private const string LegacyTokenVariable = "PM_TOKEN";
    private const string LegacyAllowInsecureVariable = "PM_ALLOW_INSECURE";

    /// <summary>
    /// 伺服器：--server、KUMI_SERVER（舊名 PM_SERVER）、設定檔，依序第一個有的；
    /// 權杖：KUMI_TOKEN（舊名 PM_TOKEN），沒有才用設定檔的。
    /// 設定檔的權杖只給當初登入的那個伺服器：換了伺服器卻還帶著它，等於把權杖交給別人。
    /// </summary>
    public static Credentials Resolve(CliContext context, string? serverOption) =>
        ResolveWith(context, serverOption, static stored => stored.Token, "kumi auth login");

    /// <summary>
    /// 本機代理（kumi agent start、kumi agent enable）用的憑證：KUMI_TOKEN（舊名 PM_TOKEN）、設定檔的 <see cref="StoredConfig.AgentToken"/>、
    /// 設定檔的 <see cref="StoredConfig.Token"/>，依序第一個有的；伺服器的規則和 <see cref="Resolve"/> 一樣。
    /// 退回用 <see cref="StoredConfig.Token"/> 是給只登入過一次的舊設定檔；範圍不夠的話本機代理自己會說要執行 kumi agent login。
    /// </summary>
    public static Credentials ResolveForAgent(CliContext context, string? serverOption) =>
        ResolveWith(context, serverOption, static stored => stored.AgentToken ?? stored.Token, "kumi agent login");

    private static Credentials ResolveWith(CliContext context, string? serverOption, Func<StoredConfig, string?> storedToken, string loginCommand)
    {
        var stored = new ConfigStore(context.ConfigPath).Load();
        var server = Normalize(Pick(serverOption, ServerFromEnvironment(context)?.Value, stored.Server)
            ?? throw new CliException(
                ExitCodes.Error, $"還沒設定伺服器。請執行 {loginCommand}，或設定環境變數 {ServerVariable}。"));

        if (TokenFromEnvironment(context) is { } fromEnvironment)
        {
            RequireTokenShape(fromEnvironment.Value, $"環境變數 {fromEnvironment.Variable}");
            return new Credentials(server, fromEnvironment.Value, TokenSource.Environment);
        }

        if (storedToken(stored) is not { } token)
        {
            throw new CliException(ExitCodes.Error, $"還沒登入。請執行 {loginCommand}，或設定環境變數 {TokenVariable}。");
        }

        if (stored.Server is null || !SameServer(stored.Server, server))
        {
            throw new CliException(
                ExitCodes.Error,
                $"存起來的權杖是 {stored.Server ?? "另一個伺服器"} 的，不會送給 {server}。"
                + $"請對這個伺服器執行 {loginCommand}，或設定環境變數 {TokenVariable}。");
        }

        RequireTokenShape(token, $"設定檔 {context.ConfigPath}");
        return new Credentials(server, token, TokenSource.ConfigFile);
    }

    /// <summary>檢查是絕對的 http／https 網址，去掉尾端的斜線。</summary>
    public static string Normalize(string server)
    {
        if (!Uri.TryCreate(server.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw CliException.Usage($"伺服器網址不正確：{StripUserInfo(server)}。請寫成 https://example.com 的樣子。");
        }

        // 網址裡的帳密會進設定檔、錯誤訊息與代理伺服器的記錄；權杖才是這裡的憑證。
        if (uri.UserInfo.Length > 0)
        {
            throw CliException.Usage($"伺服器網址不能帶帳號與密碼（http://帳號:密碼@主機）。請只寫主機，權杖用 kumi auth login 或 {TokenVariable} 給。");
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public static bool SameServer(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>連線不加密又不是本機時，權杖會在網路上明文傳送。</summary>
    public static bool IsInsecureRemote(string server) =>
        Uri.TryCreate(server, UriKind.Absolute, out var uri) && uri.Scheme == "http" && !uri.IsLoopback;

    /// <summary>
    /// 送出之前確認連線的安全：不是本機的 http 一律拒絕，除非明確放行（--insecure 或 KUMI_ALLOW_INSECURE=1，舊名 PM_ALLOW_INSECURE）；
    /// 放行時警告寫到標準錯誤，不受 --json 影響。權杖就是密碼，明文傳送等於交給路上的每一個人。
    /// </summary>
    public static void EnsureTransportAllowed(CliContext context, ParsedArguments arguments, string server)
    {
        if (!IsInsecureRemote(server))
        {
            return;
        }

        var allowed = arguments.Has(GlobalOptions.Insecure.Name)
            || FromEnvironment(context, AllowInsecureVariable, LegacyAllowInsecureVariable)?.Value is "1" or "true";
        if (!allowed)
        {
            throw CliException.Usage(
                $"伺服器 {server} 不是 https，權杖會在網路上明文傳送，所以不送出。"
                + $"確定要這樣用的話，加 --insecure，或設定環境變數 {AllowInsecureVariable}=1。");
        }

        context.Error.WriteLine($"警告：伺服器 {server} 不是 https，權杖會在網路上明文傳送。");
    }

    /// <summary>權杖是 pmt_ 開頭、沒有空白的一段文字。不對就在送出之前說，不要把一段亂碼當 Authorization 送出去。</summary>
    public static void RequireTokenShape(string token, string source)
    {
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal) || token.Any(char.IsWhiteSpace))
        {
            throw CliException.Usage(
                $"{source} 裡的不像存取權杖：權杖是 {TokenPrefix} 開頭、中間沒有空白的一段文字。請到網頁「我的帳號 → 存取權杖」建立。");
        }
    }

    /// <summary>環境變數裡的伺服器（KUMI_SERVER，沒有才看舊名 PM_SERVER）。</summary>
    public static EnvironmentValue? ServerFromEnvironment(CliContext context) =>
        FromEnvironment(context, ServerVariable, LegacyServerVariable);

    /// <summary>環境變數裡的權杖（KUMI_TOKEN，沒有才看舊名 PM_TOKEN）。</summary>
    public static EnvironmentValue? TokenFromEnvironment(CliContext context) =>
        FromEnvironment(context, TokenVariable, LegacyTokenVariable);

    /// <summary>新名稱有值就用新的，沒有才看舊名稱；連同是哪一個變數一起回，錯誤與提醒才說得對。</summary>
    private static EnvironmentValue? FromEnvironment(CliContext context, string name, string legacyName)
    {
        foreach (var variable in new[] { name, legacyName })
        {
            if (Pick(context.GetEnvironmentVariable(variable)) is { } value)
            {
                return new EnvironmentValue(value, variable);
            }
        }

        return null;
    }

    private static string StripUserInfo(string text) => text.Contains('@') ? "（含 @，已隱藏）" : text;

    private static string? Pick(params string?[] candidates) =>
        candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

/// <summary>從環境變數讀到的值，與它來自哪一個變數。</summary>
internal sealed record EnvironmentValue(string Value, string Variable);
