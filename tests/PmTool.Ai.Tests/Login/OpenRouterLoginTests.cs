using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Providers;
using PmTool.Ai.Tests.Settings;

namespace PmTool.Ai.Tests.Login;

/// <summary>用 OpenRouter 登入（internal/HANDOFF-ai-floating.md 第 4.3 節）。換金鑰的連線用假的，其餘對真的 PostgreSQL。</summary>
[TestFixture]
public sealed class OpenRouterLoginTests
{
    private const string Callback = "https://kumi.example.com/ai/openrouter/callback";

    private const string IssuedKey = "sk-or-v1-0123456789abcdef";

    private sealed class FakeAuthClient : IOpenRouterAuthClient
    {
        private readonly ConcurrentQueue<(string Code, string Verifier)> _calls = new();

        public Func<string, string, string> OnExchange { get; set; } = (_, _) => IssuedKey;

        public IReadOnlyList<(string Code, string Verifier)> Calls => _calls.ToArray();

        public Task<string> ExchangeCodeAsync(string code, string verifier, CancellationToken cancellationToken)
        {
            _calls.Enqueue((code, verifier));
            return Task.FromResult(OnExchange(code, verifier));
        }
    }

    private sealed record Setup(AiTestHost Host, FakeAuthClient Auth, ManualClock Clock)
    {
        public IOpenRouterLoginService Login => Host.Get<IOpenRouterLoginService>();
    }

    private static async Task<Setup> StartAsync(bool allowPersonal = true)
    {
        var auth = new FakeAuthClient();
        var clock = new ManualClock();
        var host = await AiTestHost.StartAsync(services => services.AddSingleton<IOpenRouterAuthClient>(auth), clock: clock);
        await host.Get<IAiSettingsService>().SaveAsync(
            AiTestData.Admin, new AiSettingsInput(false, null) { AllowPersonalConnections = allowPersonal }, CancellationToken.None);
        return new Setup(host, auth, clock);
    }

    private static async Task<(string Url, string State, string Challenge)> StartLoginAsync(
        IOpenRouterLoginService login, Actor actor, AiConnectionScope scope)
    {
        var started = await login.StartAsync(actor, scope, Callback, CancellationToken.None);
        var query = HttpUtility.ParseQueryString(new Uri(started.Value).Query);
        return (started.Value, query["state"]!, query["code_challenge"]!);
    }

    [Test]
    public async Task Start_BuildsTheAuthorizeUrlWithPkceAndAnEncryptedState()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;

        // Act
        var started = await setup.Login.StartAsync(AiTestData.Admin, AiConnectionScope.Organization, Callback, CancellationToken.None);

        // Assert
        var uri = new Uri(started.Value);
        var query = HttpUtility.ParseQueryString(uri.Query);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uri.GetLeftPart(UriPartial.Path), Is.EqualTo("https://openrouter.ai/auth"));
            Assert.That(query["callback_url"], Is.EqualTo(Callback));
            Assert.That(query["code_challenge_method"], Is.EqualTo("S256"));
            Assert.That(query["key_label"], Is.EqualTo("Kumi"));
            Assert.That(query["code_challenge"], Has.Length.EqualTo(43).And.Match("^[A-Za-z0-9_-]+$"));
            Assert.That(query["state"], Has.Length.GreaterThan(40));
            Assert.That(query["state"], Does.Not.Contain(AiTestData.Admin.PersonId.ToString()));
        }
    }

    [Test]
    public async Task Complete_SendsTheVerifierMatchingTheChallengeAndCreatesTheOrganizationConnection()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        setup.Host.Gateway.OnDiscover = _ => StubAiProviderGateway.Models("anthropic/claude-opus-5-5");
        var (_, state, challenge) = await StartLoginAsync(setup.Login, AiTestData.Admin, AiConnectionScope.Organization);

        // Act
        var done = await setup.Login.CompleteAsync(AiTestData.Admin, "auth-code-1", state, CancellationToken.None);

        // Assert
        var call = setup.Auth.Calls.Single();
        var expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(call.Verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var organization = await setup.Host.Get<IAiSettingsService>().GetAsync(AiTestData.Admin, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.Code, Is.EqualTo("auth-code-1"));
            Assert.That(call.Verifier, Has.Length.InRange(43, 128));
            Assert.That(expectedChallenge, Is.EqualTo(challenge));
            Assert.That(done.Value.Scope, Is.EqualTo(AiConnectionScope.Organization));
            Assert.That(done.Value.Connection.Name, Is.EqualTo("OpenRouter"));
            Assert.That(done.Value.Connection.Kind, Is.EqualTo(AiProviderKind.Openrouter));
            Assert.That(done.Value.Connection.ApiKeyHint, Is.EqualTo("cdef"));
            Assert.That(done.Value.Connection.Models.Select(m => (m.Id, m.Enabled)), Is.EqualTo(new[] { ("anthropic/claude-opus-5-5", false) }));
            Assert.That(setup.Host.Gateway.DiscoverCalls.Single().ApiKey, Is.EqualTo(IssuedKey));
            Assert.That(organization.Value.Connections.Select(c => c.Id), Is.EqualTo(new[] { done.Value.Connection.Id }));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(done.Value), Does.Not.Contain(IssuedKey));
        }
    }

    [Test]
    public async Task Complete_PersonalScope_CreatesMyOpenRouterWithDuplicateNamesNumbered()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        var first = await StartLoginAsync(setup.Login, AiTestData.Member, AiConnectionScope.Personal);
        var second = await StartLoginAsync(setup.Login, AiTestData.Member, AiConnectionScope.Personal);

        // Act
        var one = await setup.Login.CompleteAsync(AiTestData.Member, "c1", first.State, CancellationToken.None);
        var two = await setup.Login.CompleteAsync(AiTestData.Member, "c2", second.State, CancellationToken.None);
        var mine = await setup.Host.Get<IPersonalAiConnectionService>().ListAsync(AiTestData.Member, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(one.Value.Scope, Is.EqualTo(AiConnectionScope.Personal));
            Assert.That(one.Value.Connection.Name, Is.EqualTo("我的 OpenRouter"));
            Assert.That(two.Value.Connection.Name, Is.EqualTo("我的 OpenRouter 2"));
            Assert.That(mine, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Complete_OrganizationDuplicateName_GetsASuffix()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        await setup.Host.Get<IAiSettingsService>().CreateConnectionAsync(
            AiTestData.Admin, new AiConnectionInput("openrouter", AiProviderKind.Openrouter, null, "sk-or-v1-other-key", [], []), CancellationToken.None);
        var (_, state, _) = await StartLoginAsync(setup.Login, AiTestData.Admin, AiConnectionScope.Organization);

        // Act
        var done = await setup.Login.CompleteAsync(AiTestData.Admin, "c", state, CancellationToken.None);

        // Assert
        Assert.That(done.Value.Connection.Name, Is.EqualTo("OpenRouter 2"));
    }

    [Test]
    public async Task Start_OrganizationScopeForNonAdmin_IsForbidden()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;

        // Act
        var started = await setup.Login.StartAsync(AiTestData.Member, AiConnectionScope.Organization, Callback, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
        }
    }

    [Test]
    public async Task Start_PersonalScopeWhenNotAllowed_IsPersonalDisabled()
    {
        // Arrange
        var setup = await StartAsync(allowPersonal: false);
        await using var _ = setup.Host;

        // Act
        var started = await setup.Login.StartAsync(AiTestData.Member, AiConnectionScope.Personal, Callback, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.PersonalDisabled));
        }
    }

    [Test]
    public async Task Complete_PersonalNoLongerAllowedByTheTime_IsPersonalDisabledAndDoesNotExchange()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        var (_, state, _) = await StartLoginAsync(setup.Login, AiTestData.Member, AiConnectionScope.Personal);
        await setup.Host.Get<IAiSettingsService>().SaveAsync(AiTestData.Admin, new AiSettingsInput(false, null), CancellationToken.None);

        // Act
        var done = await setup.Login.CompleteAsync(AiTestData.Member, "c", state, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Failure?.Code, Is.EqualTo(AiErrorCodes.PersonalDisabled));
            Assert.That(setup.Auth.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Complete_AdminLostTheirRoleByTheTime_IsForbidden()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        var (_, state, _) = await StartLoginAsync(setup.Login, AiTestData.Admin, AiConnectionScope.Organization);
        var demoted = AiTestData.Admin with { IsOrganizationAdmin = false };

        // Act
        var done = await setup.Login.CompleteAsync(demoted, "c", state, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
            Assert.That(setup.Auth.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Complete_StateExpired_IsLoginFailed()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        var (_, state, _) = await StartLoginAsync(setup.Login, AiTestData.Admin, AiConnectionScope.Organization);
        setup.Clock.Advance(AiRules.OpenRouterLoginLifetime + TimeSpan.FromSeconds(2));

        // Act
        var done = await setup.Login.CompleteAsync(AiTestData.Admin, "c", state, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(done.Failure?.Code, Is.EqualTo(AiErrorCodes.LoginFailed));
            Assert.That(done.Failure?.Message, Does.StartWith("OpenRouter 的登入沒有完成：").And.Contain("太久"));
            Assert.That(setup.Auth.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Complete_StateJustBeforeExpiry_StillWorks()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        var (_, state, _) = await StartLoginAsync(setup.Login, AiTestData.Admin, AiConnectionScope.Organization);
        setup.Clock.Advance(AiRules.OpenRouterLoginLifetime - TimeSpan.FromSeconds(5));

        // Act
        var done = await setup.Login.CompleteAsync(AiTestData.Admin, "c", state, CancellationToken.None);

        // Assert
        Assert.That(done.Succeeded, Is.True);
    }

    [Test]
    public async Task Complete_StateFromAnotherPersonOrOrganization_IsLoginFailed()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        var (_, state, _) = await StartLoginAsync(setup.Login, AiTestData.Admin, AiConnectionScope.Organization);
        var anotherPerson = AiTestData.Admin with { PersonId = Guid.CreateVersion7() };
        var anotherOrganization = AiTestData.Admin with { OrganizationId = Guid.CreateVersion7() };

        // Act
        var byPerson = await setup.Login.CompleteAsync(anotherPerson, "c", state, CancellationToken.None);
        var byOrganization = await setup.Login.CompleteAsync(anotherOrganization, "c", state, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byPerson.Failure?.Code, Is.EqualTo(AiErrorCodes.LoginFailed));
            Assert.That(byOrganization.Failure?.Code, Is.EqualTo(AiErrorCodes.LoginFailed));
            Assert.That(setup.Auth.Calls, Is.Empty);
        }
    }

    [TestCase("")]
    [TestCase("not-a-state")]
    [TestCase("CfDJ8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Complete_BrokenState_IsLoginFailed(string state)
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;

        // Act
        var done = await setup.Login.CompleteAsync(AiTestData.Admin, "c", state, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Failure?.Code, Is.EqualTo(AiErrorCodes.LoginFailed));
            Assert.That(setup.Auth.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Complete_MissingCode_IsLoginFailed()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        var (_, state, _) = await StartLoginAsync(setup.Login, AiTestData.Admin, AiConnectionScope.Organization);

        // Act
        var done = await setup.Login.CompleteAsync(AiTestData.Admin, "  ", state, CancellationToken.None);

        // Assert
        Assert.That(done.Failure?.Code, Is.EqualTo(AiErrorCodes.LoginFailed));
    }

    [Test]
    public async Task Complete_ExchangeFails_IsLoginFailedWithTheReasonAndStoresNothing()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        setup.Auth.OnExchange = (_, _) => throw new AiProviderException("授權碼已經過期（10 分鐘內要完成），請重新登入。");
        var (_, state, _) = await StartLoginAsync(setup.Login, AiTestData.Admin, AiConnectionScope.Organization);

        // Act
        var done = await setup.Login.CompleteAsync(AiTestData.Admin, "c", state, CancellationToken.None);
        var settings = await setup.Host.Get<IAiSettingsService>().GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Failure?.Code, Is.EqualTo(AiErrorCodes.LoginFailed));
            Assert.That(done.Failure?.Message, Is.EqualTo("OpenRouter 的登入沒有完成：授權碼已經過期（10 分鐘內要完成），請重新登入。"));
            Assert.That(settings.Value.Connections, Is.Empty);
        }
    }

    [Test]
    public async Task Start_AtTheConnectionLimit_FailsWithoutStartingAnything()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        var personal = setup.Host.Get<IPersonalAiConnectionService>();
        for (var i = 0; i < AiRules.MaxPersonalConnections; i++)
        {
            await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic($"c{i}"), CancellationToken.None);
        }

        // Act
        var started = await setup.Login.StartAsync(AiTestData.Member, AiConnectionScope.Personal, Callback, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
            Assert.That(started.Failure?.Details.Single().Code, Is.EqualTo(AiErrorCodes.TooManyConnections));
            Assert.That(setup.Auth.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Complete_LimitReachedWhileOnOpenRouterPage_FailsWithoutExchange()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        var (_, state, _) = await StartLoginAsync(setup.Login, AiTestData.Member, AiConnectionScope.Personal);
        var personal = setup.Host.Get<IPersonalAiConnectionService>();
        for (var i = 0; i < AiRules.MaxPersonalConnections; i++)
        {
            await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic($"c{i}"), CancellationToken.None);
        }

        // Act
        var done = await setup.Login.CompleteAsync(AiTestData.Member, "c", state, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Failure?.Details.Single().Code, Is.EqualTo(AiErrorCodes.TooManyConnections));
            Assert.That(setup.Auth.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Complete_ModelListFailsAfterExchange_TellsTheUserToArchiveTheKeyAtOpenRouter()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        setup.Host.Gateway.OnDiscover = _ => throw new AiProviderException("供應商那邊出錯了，稍後再試。");
        var (_, state, _) = await StartLoginAsync(setup.Login, AiTestData.Admin, AiConnectionScope.Organization);

        // Act
        var done = await setup.Login.CompleteAsync(AiTestData.Admin, "c", state, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Failure?.Code, Is.EqualTo(AiErrorCodes.ProviderFailed));
            Assert.That(done.Failure?.Message, Does.Contain("封存"));
            Assert.That(done.Failure?.Message, Does.Not.Contain(IssuedKey));
        }
    }

    [Test]
    public async Task Complete_DoesNotWriteTheKeyToLogs()
    {
        // Arrange
        var logs = new CapturingLoggerProvider();
        var auth = new FakeAuthClient();
        await using var host = await AiTestHost.StartAsync(services =>
        {
            services.AddSingleton<IOpenRouterAuthClient>(auth);
            services.AddLogging(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        });
        await host.Get<IAiSettingsService>().SaveAsync(
            AiTestData.Admin, new AiSettingsInput(false, null), CancellationToken.None);
        var login = host.Get<IOpenRouterLoginService>();
        var (_, state, _) = await StartLoginAsync(login, AiTestData.Admin, AiConnectionScope.Organization);

        // Act
        await login.CompleteAsync(AiTestData.Admin, "c", state, CancellationToken.None);

        // Assert
        Assert.That(logs.Text, Does.Not.Contain(IssuedKey));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Text => string.Join('\n', _lines);

        public ILogger CreateLogger(string categoryName) => new Capture(_lines);

        public void Dispose()
        {
        }

        private sealed class Capture(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception) + exception);
        }
    }
}
