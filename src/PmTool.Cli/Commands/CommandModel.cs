using PmTool.Cli.Api;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Commands;

/// <summary>一個選項。Name 不含前面的 --；Value 是說明裡的參數名，空字串表示不帶值的開關。</summary>
internal sealed record OptionSpec(string Name, string Value, string Description, char? Short = null, bool Repeatable = false)
{
    public bool TakesValue => Value.Length > 0;
}

internal sealed record CommandSpec(
    string Group,
    string Name,
    string Summary,
    string Usage,
    IReadOnlyList<OptionSpec> Options,
    int MinArguments,
    int MaxArguments,
    Func<CommandCall, Task<int>> Run)
{
    /// <summary>名稱是空的，指令就是群組本身（kumi search 文字），沒有第二層。</summary>
    public string Path => Name.Length == 0 ? Group : $"{Group} {Name}";
}

/// <summary>每個指令都有的選項。</summary>
internal static class GlobalOptions
{
    public static readonly OptionSpec Json = new("json", "", "輸出伺服器回的資料（JSON），給腳本用");
    public static readonly OptionSpec Server = new("server", "網址", "伺服器網址（預設用 KUMI_SERVER 或登入時存的）");
    public static readonly OptionSpec Insecure = new("insecure", "", "允許用 http 連到不是本機的伺服器（權杖會明文傳送，也可以設 PM_ALLOW_INSECURE=1）");
    public static readonly OptionSpec Help = new("help", "", "顯示這個指令的說明", 'h');

    public static readonly IReadOnlyList<OptionSpec> All = [Json, Server, Insecure, Help];
}

/// <summary>解析好的指令列：位置參數與選項的值。</summary>
internal sealed class ParsedArguments(IReadOnlyList<string> positionals, IReadOnlyDictionary<string, List<string>> values)
{
    public IReadOnlyList<string> Positionals { get; } = positionals;

    public bool Has(string name) => values.ContainsKey(name);

    public string? Value(string name) => values.TryGetValue(name, out var list) ? list[^1] : null;

    public IReadOnlyList<string> Values(string name) => values.TryGetValue(name, out var list) ? list : [];

    public string Required(string name) =>
        Value(name) ?? throw CliException.Usage($"缺少 --{name}。");
}

/// <summary>一次指令執行：環境、解析好的參數，以及需要時才建立的 API 連線。</summary>
internal sealed class CommandCall(CliContext context, ParsedArguments arguments, CancellationToken cancellationToken)
{
    private ApiClient? _api;

    public CliContext Context { get; } = context;

    public ParsedArguments Arguments { get; } = arguments;

    public CancellationToken CancellationToken { get; } = cancellationToken;

    public TextWriter Out => Context.Out;

    public TextWriter Error => Context.Error;

    public bool Json => Arguments.Has("json");

    /// <summary>第一次用到才解析伺服器與權杖：沒登入時，不需要連線的指令（例如本機驗證）照樣能跑。</summary>
    public ApiClient Api => _api ??= Connect();

    public Credentials Credentials { get; private set; } = null!;

    private ApiClient Connect()
    {
        Credentials = Credentials.Resolve(Context, Arguments.Value("server"));
        Credentials.EnsureTransportAllowed(Context, Arguments, Credentials.Server);
        return new ApiClient(Context.Http, Credentials.Server, Credentials.Token);
    }
}
