using Microsoft.EntityFrameworkCore;
using Npgsql;
using static PmTool.Identity.Tests.OAuth.OAuthTestSupport;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>
/// 撤銷與兌換、輪替同時發生：另一條連線先鎖住授權那一列（模擬撤銷做到一半），兌換與輪替要等它，
/// 等到撤銷提交之後重新檢查，不能在撤銷之後還發出有效的權杖。
/// </summary>
[TestFixture]
public sealed class OAuthRevokeRaceTests
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(500);

    private readonly ManualTimeProvider _time = new();
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;
    private AuthenticatedUser _user = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await OAuthTestSupport.StartAsync(_time);
        _owner = await _host.StartWithOwnerAsync();
    }

    [SetUp]
    public async Task AddUserAsync() => _user = await _host.AddUserAsync(_owner.ToActor());

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ExchangeCode_WhileTheGrantIsBeingRevoked_WaitsAndThenIssuesNothing()
    {
        // Arrange
        var client = await _host.RegisterAsync();
        var (code, verifier) = await _host.ApproveAsync(_user, client.ClientId);
        var grantId = (await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None)).Single().GrantId;
        await using var revoking = await BeginRevokingAsync(grantId);

        // Act
        var exchange = _host.ExchangeAsync(client.ClientId, code, verifier);
        await Task.Delay(SettleTime);
        var finishedEarly = exchange.IsCompleted;
        await revoking.CommitAsync();
        var result = await exchange;

        // Assert
        Assert.That(finishedEarly, Is.False, "授權那一列被鎖住時，兌換要等");
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await ActiveAccessTokensAsync(grantId), Is.Zero);
    }

    [Test]
    public async Task Refresh_WhileTheGrantIsBeingRevoked_WaitsAndThenIssuesNothing()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);
        var grantId = (await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None)).Single().GrantId;
        var before = await ActiveAccessTokensAsync(grantId);
        await using var revoking = await BeginRevokingAsync(grantId);

        // Act
        var refresh = _host.RefreshAsync(client.ClientId, tokens.RefreshToken);
        await Task.Delay(SettleTime);
        var finishedEarly = refresh.IsCompleted;
        await revoking.CommitAsync();
        var result = await refresh;

        // Assert
        Assert.That(finishedEarly, Is.False, "授權那一列被鎖住時，輪替要等");
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await ActiveAccessTokensAsync(grantId), Is.EqualTo(before), "不能多發出新的權杖");
    }

    [Test]
    public async Task Refresh_AfterTheGrantWasRevokedByAnotherProcess_IsInvalidGrantWithoutIssuingAnything()
    {
        // Arrange
        var (client, tokens) = await _host.ConnectAsync(_user);
        var grantId = (await _host.OAuth.ListGrantsAsync(_user.ToActor(), CancellationToken.None)).Single().GrantId;
        var before = await ActiveAccessTokensAsync(grantId);
        await using (var revoking = await BeginRevokingAsync(grantId))
        {
            await revoking.CommitAsync();
        }

        // Act
        var result = await _host.RefreshAsync(client.ClientId, tokens.RefreshToken);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidGrant));
        Assert.That(await ActiveAccessTokensAsync(grantId), Is.EqualTo(before));
    }

    private async Task<RevokingTransaction> BeginRevokingAsync(Guid grantId)
    {
        var connection = new NpgsqlConnection(_host.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE identity.oauth_grants SET revoked_at = now(), revoked_reason = 'test' WHERE id = @id", connection, transaction);
        command.Parameters.AddWithValue("id", grantId);
        await command.ExecuteNonQueryAsync();
        return new RevokingTransaction(connection, transaction);
    }

    private async Task<int> ActiveAccessTokensAsync(Guid grantId)
    {
        await using var db = await _host.CreateDbContextAsync();
        return await db.AccessTokens.CountAsync(t => t.GrantId == grantId && t.RevokedAt == null && t.ExpiresAt > _time.GetUtcNow());
    }

    private sealed class RevokingTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public Task CommitAsync() => transaction.CommitAsync();

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
