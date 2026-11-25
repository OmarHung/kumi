using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Cli;
using PmTool.Identity;

namespace PmTool.Api.Tests.RealStack;

internal sealed record CliOutcome(int ExitCode, string Out, string Error);

/// <summary>
/// 用真的存取權杖，經過真的 API 與資料庫跑 CLI 的指令：CLI 的程式庫收的是 WebApplicationFactory 的 HttpClient，
/// 和使用者在終端機上跑 pm 走的是同一條路，只是不經過網路與終端機。
/// </summary>
internal sealed class CliSession : IDisposable
{
    public const string Server = "http://localhost";

    private readonly RealStackFactory _factory;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pm-cli-e2e-{Guid.NewGuid():N}");

    private CliSession(RealStackFactory factory, CreatedAccessToken token, Actor actor)
    {
        _factory = factory;
        Token = token;
        Actor = actor;
        Directory.CreateDirectory(_directory);
        ConfigPath = Path.Combine(_directory, "config", "config.json");
    }

    public CreatedAccessToken Token { get; private set; }

    public Actor Actor { get; }

    public string ConfigPath { get; }

    /// <summary>登入的人替自己建一把有這些範圍的權杖，再用它跑指令。</summary>
    public static async Task<CliSession> CreateAsync(
        RealStackFactory factory, SignedInClient person, params AccessTokenScope[] scopes)
    {
        var session = await RealStackSteps.GetAsync(person.Client, "/v1/session");
        var user = session["user"]!;
        var actor = new Actor(
            user["organizationId"]!.GetValue<Guid>(),
            person.PersonId,
            user["role"]!.GetValue<string>() is "owner" or "admin");
        var created = await factory.Services.GetRequiredService<IAccessTokenService>().CreateAsync(
            actor, new NewAccessToken($"cli-{Guid.NewGuid():N}", scopes, ExpiresInDays: 30), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        return new CliSession(factory, created.Value, actor);
    }

    /// <summary>命令列用得到的全部範圍。<c>agent:run</c> 是本機代理的權杖專用的，帶了它就打不了一般的端點，也不能和 <c>items:write</c> 放在同一把。</summary>
    public static AccessTokenScope[] AllScopes => [.. AccessTokenScopes.All.Where(scope => scope != AccessTokenScope.AgentRun)];

    /// <summary>用環境變數帶權杖（和 CI 一樣）跑一個指令。</summary>
    public Task<CliOutcome> RunAsync(params string[] args) => RunWithInputAsync(string.Empty, args);

    public async Task<CliOutcome> RunWithInputAsync(string standardInput, params string[] args)
    {
        using var http = _factory.CreateClient();
        var output = new StringWriter();
        var error = new StringWriter();
        var environment = new Dictionary<string, string?> { ["PM_SERVER"] = Server, ["PM_TOKEN"] = Token.Secret };
        var context = new CliContext
        {
            Http = http,
            Out = output,
            Error = error,
            In = new StringReader(standardInput),
            ConfigPath = ConfigPath,
            GetEnvironmentVariable = name => environment.GetValueOrDefault(name),
        };
        var exitCode = await CliRunner.RunAsync(args, context, CancellationToken.None);
        return new CliOutcome(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>不帶環境變數，只靠設定檔（測 auth login 存下來的東西）。</summary>
    public async Task<CliOutcome> RunWithConfigFileAsync(string standardInput, params string[] args)
    {
        using var http = _factory.CreateClient();
        var output = new StringWriter();
        var error = new StringWriter();
        var context = new CliContext
        {
            Http = http,
            Out = output,
            Error = error,
            In = new StringReader(standardInput),
            ConfigPath = ConfigPath,
        };
        var exitCode = await CliRunner.RunAsync(args, context, CancellationToken.None);
        return new CliOutcome(exitCode, output.ToString(), error.ToString());
    }

    public Task<Result<AccessTokenSummary>> RevokeAsync() =>
        _factory.Services.GetRequiredService<IAccessTokenService>().RevokeAsync(Actor, Token.Token.TokenId, CancellationToken.None);

    public string PathOf(string name) => Path.Combine(_directory, name);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
