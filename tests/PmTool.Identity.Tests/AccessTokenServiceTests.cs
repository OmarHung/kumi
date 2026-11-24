using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>個人存取權杖：建立、列出、撤銷與驗證規則。</summary>
[TestFixture]
public sealed class AccessTokenServiceTests
{
    private static readonly AccessTokenScope[] Read = [AccessTokenScope.ItemsRead];

    private readonly ManualTimeProvider _time = new();
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await SessionTestSupport.StartWithClockAsync(_time);
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task Create_ReturnsThePlaintextOnceWithThePrefixAndASummary()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.AccessTokens.CreateAsync(
            user.ToActor(), new NewAccessToken("  CI  ", [AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintWrite], 30), CancellationToken.None);

        // Assert
        var created = result.Value;
        Assert.That(created.Secret, Does.StartWith(AccessTokenRules.Prefix));
        Assert.That(created.Token.DisplayPrefix, Is.EqualTo(created.Secret[..8]));
        Assert.That(created.Token.Name, Is.EqualTo("CI"));
        Assert.That(created.Token.Scopes, Is.EqualTo(new[] { AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintWrite }));
        Assert.That(created.Token.CreatedAt, Is.EqualTo(_time.GetUtcNow()));
        Assert.That(created.Token.ExpiresAt, Is.EqualTo(_time.GetUtcNow().AddDays(30)));
        Assert.That(created.Token.LastUsedAt, Is.Null);
    }

    [Test]
    public async Task Create_AgentRunScope_IsStoredListedAndFoundWithTheToken()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var scopes = new[] { AccessTokenScope.AgentRun, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite };

        // Act
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("本機代理（電腦）", scopes, 90), CancellationToken.None)).Value;
        var listed = (await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None)).Single(t => t.TokenId == created.Token.TokenId);
        var found = await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AccessTokenScopes.NameOf(AccessTokenScope.AgentRun), Is.EqualTo("agent:run"));
            Assert.That(AccessTokenScopes.TryParse("agent:run", out var parsed) && parsed == AccessTokenScope.AgentRun, Is.True);
            Assert.That(created.Token.Scopes, Is.EqualTo(scopes));
            Assert.That(listed.Scopes, Is.EqualTo(scopes));
            Assert.That(found?.Scopes, Is.EqualTo(scopes));
        }
    }

    [Test]
    public void Grantable_IsEveryScopeExceptAgentRun()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AccessTokenScopes.All, Does.Contain(AccessTokenScope.AgentRun));
            Assert.That(AccessTokenScopes.Grantable, Does.Not.Contain(AccessTokenScope.AgentRun));
            Assert.That(
                AccessTokenScopes.Grantable,
                Is.EqualTo(new[] { AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite }));
        }
    }

    [Test]
    public async Task Create_WithoutExpiry_NeverExpires()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("永久", Read, null), CancellationToken.None)).Value;

        // Act
        _time.Advance(TimeSpan.FromDays(3000));
        var found = await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None);

        // Assert
        Assert.That(created.Token.ExpiresAt, Is.Null);
        Assert.That(found?.TokenId, Is.EqualTo(created.Token.TokenId));
    }

    [TestCase("", AccessTokenErrors.NameRequired, "name")]
    [TestCase("   ", AccessTokenErrors.NameRequired, "name")]
    [TestCase("a\nb", AccessTokenErrors.NameRequired, "name")]
    [TestCase("a\0b", AccessTokenErrors.NameRequired, "name")]
    [TestCase("a\u200Eb", AccessTokenErrors.NameRequired, "name")]
    [TestCase("a\u202Eb", AccessTokenErrors.NameRequired, "name")]
    [TestCase("a\u200Bb", AccessTokenErrors.NameRequired, "name")]
    [TestCase("a\uFEFFb", AccessTokenErrors.NameRequired, "name")]
    [TestCase("a\u2028b", AccessTokenErrors.NameRequired, "name")]
    [TestCase("a\u2029b", AccessTokenErrors.NameRequired, "name")]
    public async Task Create_BadName_IsInvalidWithThePathOfTheField(string name, string code, string path)
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken(name, Read, 30), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
        Assert.That(result.Failure?.Details?.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (code, path) }));
    }

    [Test]
    public async Task Create_NameWithOrdinaryNonAsciiText_IsAccepted()
    {
        // Arrange：擋格式字元不能連一般的中文、日文、表情符號與全形空白一起擋掉。
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.AccessTokens.CreateAsync(
            user.ToActor(), new NewAccessToken("部署用　トークン 🚀", Read, 30), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [Test]
    public async Task Create_ConcurrentRequests_NeverGoOverTheLimit()
    {
        // Arrange：同時送出的數量比上限多，用一個起跑線讓它們盡量同時進到建立的交易。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var actor = user.ToActor();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, AccessTokenRules.MaxActiveTokensPerAccount + 10)
            .Select(async i =>
            {
                await start.Task;
                return await _host.AccessTokens.CreateAsync(actor, new NewAccessToken($"c{i}", Read, 30), CancellationToken.None);
            })
            .ToArray();

        // Act
        start.SetResult();
        var results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(60));

        // Assert
        var listed = await _host.AccessTokens.ListAsync(actor, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(AccessTokenRules.MaxActiveTokensPerAccount));
            Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure?.Code), Is.All.EqualTo(IdentityErrorCodes.AccessTokenLimitReached));
            Assert.That(listed, Has.Count.EqualTo(AccessTokenRules.MaxActiveTokensPerAccount));
        }
    }

    [Test]
    public async Task Create_NameOf100CharactersIsAcceptedAndOf101IsTooLong()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var ok = await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken(new string('a', 100), Read, 30), CancellationToken.None);
        var tooLong = await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken(new string('a', 101), Read, 30), CancellationToken.None);

        // Assert
        Assert.That(ok.Succeeded, Is.True);
        Assert.That(tooLong.Failure?.Code, Is.EqualTo(IdentityErrorCodes.AccessTokenNameTooLong));
        Assert.That(tooLong.Failure?.Details?.Single().Path, Is.EqualTo("name"));
    }

    [Test]
    public async Task Create_NoScopesOrDuplicateScopes_IsScopesRequired()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var none = await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("x", [], 30), CancellationToken.None);
        var duplicated = await _host.AccessTokens.CreateAsync(
            user.ToActor(), new NewAccessToken("x", [AccessTokenScope.ItemsRead, AccessTokenScope.ItemsRead], 30), CancellationToken.None);

        // Assert
        Assert.That(none.Failure?.Code, Is.EqualTo(IdentityErrorCodes.AccessTokenScopesRequired));
        Assert.That(none.Failure?.Details?.Single().Path, Is.EqualTo("scopes"));
        Assert.That(duplicated.Failure?.Code, Is.EqualTo(IdentityErrorCodes.AccessTokenScopesRequired));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(366)]
    public async Task Create_ExpiryOutsideOneTo365Days_IsInvalid(int days)
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("x", Read, days), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.AccessTokenExpiryInvalid));
        Assert.That(result.Failure?.Details?.Single().Path, Is.EqualTo("expiresInDays"));
    }

    [Test]
    public async Task Create_SeveralProblems_AreAllListed()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken(" ", [], 0), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details?.Select(d => d.Path), Is.EquivalentTo(new[] { "name", "scopes", "expiresInDays" }));
    }

    [Test]
    public async Task Create_TheTwentyFirstActiveToken_IsRejectedButExpiredAndRevokedOnesDoNotCount()
    {
        // Arrange：先建滿 20 把（第一把一天後過期）；撤銷一把、再讓第一把過期，空出兩個位置。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var actor = user.ToActor();
        var tokens = new List<CreatedAccessToken>();
        for (var i = 0; i < AccessTokenRules.MaxActiveTokensPerAccount; i++)
        {
            tokens.Add((await _host.AccessTokens.CreateAsync(actor, new NewAccessToken($"t{i}", Read, i == 0 ? 1 : 30), CancellationToken.None)).Value);
        }

        var blocked = await _host.AccessTokens.CreateAsync(actor, new NewAccessToken("extra", Read, 30), CancellationToken.None);
        await _host.AccessTokens.RevokeAsync(actor, tokens[1].Token.TokenId, CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(2));
        var afterRevoke = await _host.AccessTokens.CreateAsync(actor, new NewAccessToken("extra", Read, 30), CancellationToken.None);

        // Act：撤銷的與過期的不算，現在有 19 把，再建一把剛好 20。
        var full = await _host.AccessTokens.CreateAsync(actor, new NewAccessToken("extra2", Read, 30), CancellationToken.None);

        // Assert
        Assert.That(blocked.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
        Assert.That(blocked.Failure?.Code, Is.EqualTo(IdentityErrorCodes.AccessTokenLimitReached));
        Assert.That(afterRevoke.Succeeded, Is.True);
        Assert.That(full.Succeeded, Is.True, full.Failure?.Message);
        Assert.That((await _host.AccessTokens.CreateAsync(actor, new NewAccessToken("extra3", Read, 30), CancellationToken.None)).Failure?.Code,
            Is.EqualTo(IdentityErrorCodes.AccessTokenLimitReached));
    }

    [Test]
    public async Task List_ShowsOnlyMyUnrevokedTokensNewestFirstAndKeepsExpiredOnes()
    {
        // Arrange
        var me = await _host.AddUserAsync(_owner.ToActor());
        var other = await _host.AddUserAsync(_owner.ToActor());
        var first = (await _host.AccessTokens.CreateAsync(me.ToActor(), new NewAccessToken("first", Read, 1), CancellationToken.None)).Value;
        _time.Advance(TimeSpan.FromMinutes(1));
        var second = (await _host.AccessTokens.CreateAsync(me.ToActor(), new NewAccessToken("second", Read, 30), CancellationToken.None)).Value;
        var revoked = (await _host.AccessTokens.CreateAsync(me.ToActor(), new NewAccessToken("revoked", Read, 30), CancellationToken.None)).Value;
        await _host.AccessTokens.CreateAsync(other.ToActor(), new NewAccessToken("theirs", Read, 30), CancellationToken.None);
        await _host.AccessTokens.RevokeAsync(me.ToActor(), revoked.Token.TokenId, CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(2));

        // Act
        var listed = await _host.AccessTokens.ListAsync(me.ToActor(), CancellationToken.None);

        // Assert
        Assert.That(listed.Select(t => t.TokenId), Is.EqualTo(new[] { second.Token.TokenId, first.Token.TokenId }));
    }

    [Test]
    public async Task Revoke_OwnToken_ReturnsItAndStopsItWorkingRightAway()
    {
        // Arrange：先查過一次，結果進了快取。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("x", Read, 30), CancellationToken.None)).Value;
        Assert.That(await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None), Is.Not.Null);

        // Act
        var result = await _host.AccessTokens.RevokeAsync(user.ToActor(), created.Token.TokenId, CancellationToken.None);

        // Assert
        Assert.That(result.Value.TokenId, Is.EqualTo(created.Token.TokenId));
        Assert.That(await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Revoke_SomeoneElsesAlreadyRevokedOrUnknownToken_IsNotFound()
    {
        // Arrange
        var me = await _host.AddUserAsync(_owner.ToActor());
        var other = await _host.AddUserAsync(_owner.ToActor());
        var theirs = (await _host.AccessTokens.CreateAsync(other.ToActor(), new NewAccessToken("x", Read, 30), CancellationToken.None)).Value;
        var mine = (await _host.AccessTokens.CreateAsync(me.ToActor(), new NewAccessToken("x", Read, 30), CancellationToken.None)).Value;
        await _host.AccessTokens.RevokeAsync(me.ToActor(), mine.Token.TokenId, CancellationToken.None);

        // Act
        var results = new[]
        {
            await _host.AccessTokens.RevokeAsync(me.ToActor(), theirs.Token.TokenId, CancellationToken.None),
            await _host.AccessTokens.RevokeAsync(me.ToActor(), mine.Token.TokenId, CancellationToken.None),
            await _host.AccessTokens.RevokeAsync(me.ToActor(), Guid.NewGuid(), CancellationToken.None),
        };

        // Assert
        Assert.That(results.Select(r => (r.Failure?.Kind, r.Failure?.Code)), Is.All.EqualTo((FailureKind.NotFound, IdentityErrorCodes.AccessTokenNotFound)));
        Assert.That(await _host.AccessTokens.FindAsync(theirs.Secret, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task Find_ValidToken_ReturnsTheUserScopesAndRecordsLastUsed()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var scopes = new[] { AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead };
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("x", scopes, 30), CancellationToken.None)).Value;
        _time.Advance(TimeSpan.FromHours(1));

        // Act
        var found = await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None);
        var listed = (await _host.AccessTokens.ListAsync(user.ToActor(), CancellationToken.None)).Single();

        // Assert
        Assert.That(found, Is.EqualTo(new ActiveAccessToken(created.Token.TokenId, user, scopes)).Using<ActiveAccessToken>(SameToken));
        Assert.That(listed.LastUsedAt, Is.EqualTo(_time.GetUtcNow()));
    }

    [TestCase("")]
    [TestCase("pmt_")]
    [TestCase("pmt_short")]
    [TestCase("not-a-token")]
    public async Task Find_MalformedToken_IsNullWithoutTouchingTheDatabase(string token)
    {
        // Arrange：連不上的資料庫，一碰就會丟例外。
        await using var host = IdentityTestHost.StartWithoutDatabase(null);

        // Act
        var found = await host.AccessTokens.FindAsync(token, CancellationToken.None);

        // Assert
        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task Find_SessionTokenOrUnknownWellFormedToken_IsNull()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);

        // Act
        var viaSession = await _host.AccessTokens.FindAsync(AccessTokenRules.Prefix + session.Token, CancellationToken.None);
        var unknown = await _host.AccessTokens.FindAsync(AccessTokenRules.Prefix + session.Token.Replace('A', 'B'), CancellationToken.None);

        // Assert
        Assert.That(viaSession, Is.Null);
        Assert.That(unknown, Is.Null);
    }

    [Test]
    public async Task Find_Expired_IsNullEvenWhenItWasCached()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("x", Read, 1), CancellationToken.None)).Value;
        Assert.That(await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None), Is.Not.Null);

        // Act
        _time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Find_AccountDisabled_IsNull()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("x", Read, 30), CancellationToken.None)).Value;

        // Act
        await _host.DisableAccountAsync(user.AccountId);
        _time.Advance(TimeSpan.FromMinutes(1));

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Disable_RevokesTheirTokensRightAwayAndEnablingDoesNotBringThemBack()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        var bystander = await _host.AddUserAsync(_owner.ToActor());
        var created = (await _host.AccessTokens.CreateAsync(target.ToActor(), new NewAccessToken("x", Read, 30), CancellationToken.None)).Value;
        var other = (await _host.AccessTokens.CreateAsync(bystander.ToActor(), new NewAccessToken("x", Read, 30), CancellationToken.None)).Value;
        await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None);

        // Act
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, CancellationToken.None);
        var whileDisabled = await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None);
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, false, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(1));

        // Assert
        Assert.That(whileDisabled, Is.Null);
        Assert.That(await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None), Is.Null);
        Assert.That(await _host.AccessTokens.ListAsync(target.ToActor(), CancellationToken.None), Is.Empty);
        Assert.That(await _host.AccessTokens.FindAsync(other.Secret, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task ResetPassword_RevokesTheirTokensRightAway()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        var created = (await _host.AccessTokens.CreateAsync(target.ToActor(), new NewAccessToken("x", Read, 30), CancellationToken.None)).Value;
        await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None);

        // Act
        await _host.People.ResetPasswordAsync(_owner.ToActor(), target.PersonId, "another-password-1", CancellationToken.None);

        // Assert
        Assert.That(await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ChangeOwnPassword_KeepsTheTokens()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("x", Read, 30), CancellationToken.None)).Value;

        // Act
        var changed = await _host.Accounts.ChangeOwnPasswordAsync(session.Token, TestData.Password, "brand-new-password", CancellationToken.None);

        // Assert
        Assert.That(changed.Succeeded, Is.True, changed.Failure?.Message);
        Assert.That(await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task Find_RepeatedWithinTheCacheWindow_DoesNotQueryAgain()
    {
        // Arrange：換掉資料庫之後同一個程序還是拿得到快取的結果。以「直接撤銷資料列、不經過服務」模擬別的程序撤銷。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("x", Read, 30), CancellationToken.None)).Value;
        await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None);

        // Act
        await _host.RevokeAccessTokenBehindTheCacheAsync(created.Token.TokenId);
        var cached = await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(IdentityRules.SessionCacheSeconds + 1));
        var refreshed = await _host.AccessTokens.FindAsync(created.Secret, CancellationToken.None);

        // Assert
        Assert.That(cached, Is.Not.Null);
        Assert.That(refreshed, Is.Null);
    }

    [Test]
    public async Task Find_PrefixPlusALiveSessionToken_DoesNotCollideWithTheSession()
    {
        // Arrange：pmt_ 加工作階段的權杖，雜湊和那個工作階段一樣；兩種權杖的快取鍵不能撞在一起。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var viaSession = await _host.Sessions.FindAsync(session.Token, CancellationToken.None);

        // Act
        var asAccessToken = await _host.AccessTokens.FindAsync(AccessTokenRules.Prefix + session.Token, CancellationToken.None);
        var sessionAgain = await _host.Sessions.FindAsync(session.Token, CancellationToken.None);

        // Assert
        Assert.That(viaSession, Is.Not.Null);
        Assert.That(asAccessToken, Is.Null);
        Assert.That(sessionAgain, Is.Not.Null);
    }

    [Test]
    public async Task Create_OnlyStoresTheHashOfTheToken()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var created = (await _host.AccessTokens.CreateAsync(user.ToActor(), new NewAccessToken("x", Read, 30), CancellationToken.None)).Value;

        // Assert：這一條破例直接看資料表，理由和 SessionStorageTests 一樣。
        var row = await _host.ReadAccessTokenRowAsync(created.Token.TokenId);
        var plain = System.Text.Encoding.UTF8.GetBytes(created.Secret);
        var bytes = System.Buffers.Text.Base64Url.DecodeFromChars(created.Secret.AsSpan(AccessTokenRules.Prefix.Length));
        Assert.That(row.TokenHash, Is.EqualTo(System.Security.Cryptography.SHA256.HashData(bytes)));
        Assert.That(row.TokenHash, Is.Not.EqualTo(plain));
        Assert.That(row.OrganizationId, Is.EqualTo(user.OrganizationId));
        Assert.That(row.AccountId, Is.EqualTo(user.AccountId));
        Assert.That(row.DisplayPrefix, Is.EqualTo(created.Secret[..8]));
    }

    private static bool SameToken(ActiveAccessToken? left, ActiveAccessToken? right) =>
        left is not null && right is not null && left.TokenId == right.TokenId && left.User == right.User
        && left.Scopes.SequenceEqual(right.Scopes);
}

/// <summary>錯誤代碼常數的別名，TestCase 的屬性值要常數。</summary>
internal static class AccessTokenErrors
{
    public const string NameRequired = "access_token.name_required";
}
