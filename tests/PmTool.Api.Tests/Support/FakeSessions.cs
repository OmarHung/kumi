using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Api.Tests.Support;

/// <summary>記憶體裡的一個工作階段。</summary>
internal sealed record FakeSession(long Sequence, Guid SessionId, Guid AccountId, DateTimeOffset ExpiresAt, bool Ended);

/// <summary>
/// 測試用的伺服器端工作階段。權杖不是真的亂數，但一樣只能從這裡拿到；帳號現在的資料問 <see cref="FakeAuthenticationService"/>，
/// 所以測試停用帳號或改名稱，下一次查詢就看得到。測試也可以直接作廢某個工作階段，模擬別的裝置登出或改了密碼。
/// 閒置與絕對期限是身分模組的規則，在它自己的測試裡測；這裡只照絕對期限擋，讓 API 傳下去的期限看得出效果。
/// </summary>
internal sealed class FakeSessionService(FakeAuthenticationService accounts, TimeProvider time) : ISessionService
{
    private readonly ConcurrentDictionary<string, FakeSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<AuthenticatedUser> _startCalls = new();
    private readonly ConcurrentQueue<string> _findCalls = new();
    private readonly ConcurrentQueue<string> _endCalls = new();
    private readonly ConcurrentQueue<string> _endOthersCalls = new();
    private readonly ConcurrentQueue<string> _endOthersPasswords = new();
    private long _sequence;

    public IReadOnlyList<AuthenticatedUser> StartCalls => _startCalls.ToArray();

    public IReadOnlyList<string> FindCalls => _findCalls.ToArray();

    public IReadOnlyList<string> EndCalls => _endCalls.ToArray();

    public IReadOnlyList<string> EndOthersCalls => _endOthersCalls.ToArray();

    /// <summary>每次 EndOthersAsync 收到的目前密碼，依呼叫的順序。</summary>
    public IReadOnlyList<string> EndOthersPasswords => _endOthersPasswords.ToArray();

    /// <summary>指定 EndOthersAsync 的回答，模擬確認登入之後、換工作階段之前它被作廢了（回 SessionEnded）。</summary>
    public Func<string, Result<SessionRotation>>? OnEndOthers { get; set; }

    /// <summary>每次 FindAsync 記下呼叫之後、回答之前會等它，測試用來讓查詢停在半路。</summary>
    public Func<string, Task>? BeforeFind { get; set; }

    /// <summary>這個帳號開過的工作階段的權杖，依開的順序。</summary>
    public IReadOnlyList<string> TokensOf(Guid accountId) =>
        [.. _sessions.Where(pair => pair.Value.AccountId == accountId).OrderBy(pair => pair.Value.Sequence).Select(pair => pair.Key)];

    public bool IsActive(string token) => _sessions.TryGetValue(token, out var session) && !session.Ended;

    /// <summary>作廢一個工作階段，模擬別的地方讓它失效。</summary>
    public void End(string token)
    {
        if (_sessions.TryGetValue(token, out var session))
        {
            _sessions[token] = session with { Ended = true };
        }
    }

    /// <summary>不記成一次查詢的 <see cref="FindAsync"/>，給其他假服務用，測試數得出 API 自己查了幾次。</summary>
    public ActiveSession? Peek(string token) =>
        Active(token) is { } found && accounts.Current(found.AccountId) is { } user
            ? new ActiveSession(found.SessionId, user, found.ExpiresAt)
            : null;

    /// <summary>照真的模組：密碼對、帳號沒停用才開工作階段。</summary>
    public async Task<SignedIn?> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (await accounts.SignInAsync(email, password, cancellationToken) is not { } user)
        {
            return null;
        }

        return new SignedIn(user, await StartAsync(user, cancellationToken));
    }

    /// <summary>直接替帳號開一個工作階段（不經過密碼），測試用來準備已經登入的狀態。</summary>
    public Task<StartedSession> StartAsync(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        _startCalls.Enqueue(user);
        var token = $"fake-session-{Guid.NewGuid():N}";
        var session = new FakeSession(
            Interlocked.Increment(ref _sequence), Guid.CreateVersion7(), user.AccountId,
            time.GetUtcNow().AddDays(IdentityRules.SessionAbsoluteDays), Ended: false);
        _sessions[token] = session;
        return Task.FromResult(new StartedSession(session.SessionId, token, session.ExpiresAt));
    }

    public async Task<ActiveSession?> FindAsync(string token, CancellationToken cancellationToken)
    {
        _findCalls.Enqueue(token);
        if (BeforeFind is { } wait)
        {
            await wait(token);
        }

        return Peek(token);
    }

    public Task EndAsync(string token, CancellationToken cancellationToken)
    {
        _endCalls.Enqueue(token);
        End(token);
        return Task.CompletedTask;
    }

    public Task<bool> IsActiveAsync(string token, CancellationToken cancellationToken) => Task.FromResult(Peek(token) is not null);

    /// <summary>照真的規則：目前的密碼要對（不對是 account.password_incorrect）、其他的作廢、這一個換成新的（沿用舊的期限）；已經無效是 session_ended。</summary>
    public Task<Result<SessionRotation>> EndOthersAsync(string token, string currentPassword, CancellationToken cancellationToken)
    {
        _endOthersCalls.Enqueue(token);
        _endOthersPasswords.Enqueue(currentPassword);
        if (OnEndOthers is { } respond)
        {
            return Task.FromResult(respond(token));
        }

        if (Peek(token) is not { } session)
        {
            return Task.FromResult<Result<SessionRotation>>(SessionEnded);
        }

        return Task.FromResult(
            accounts.HasPassword(session.User.Email, currentPassword)
                ? Rotate(token)
                : new Failure(FailureKind.Invalid, IdentityErrorCodes.PasswordIncorrect, "目前的密碼不正確。")
                {
                    Details = [new FailureDetail(IdentityErrorCodes.PasswordIncorrect, "currentPassword", "目前的密碼不正確。")],
                });
    }

    internal static readonly Failure SessionEnded =
        new(FailureKind.Forbidden, IdentityErrorCodes.SessionEnded, "工作階段已經失效，請重新登入。");

    /// <summary>不問密碼的換發，給假的改密碼用。</summary>
    internal Result<SessionRotation> Rotate(string token)
    {
        if (Active(token) is not { } current)
        {
            return SessionEnded;
        }

        var others = _sessions
            .Where(pair => pair.Key != token && pair.Value.AccountId == current.AccountId && !pair.Value.Ended)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var other in others)
        {
            End(other);
        }

        End(token);
        return new SessionRotation(others.Length, Replace(current, current.ExpiresAt));
    }

    /// <summary>替同一個帳號開一個新的工作階段（換掉舊的那一個），期限由呼叫端決定。</summary>
    public StartedSession Replace(FakeSession previous, DateTimeOffset expiresAt)
    {
        var token = $"fake-session-{Guid.NewGuid():N}";
        var session = new FakeSession(Interlocked.Increment(ref _sequence), Guid.CreateVersion7(), previous.AccountId, expiresAt, Ended: false);
        _sessions[token] = session;
        return new StartedSession(session.SessionId, token, expiresAt);
    }

    /// <summary>目前還有效的工作階段（給假的帳號服務用）。</summary>
    public FakeSession? ActiveSession(string token) => Active(token);

    private FakeSession? Active(string token) =>
        _sessions.TryGetValue(token, out var session) && !session.Ended && time.GetUtcNow() < session.ExpiresAt ? session : null;
}

internal sealed record ChangePasswordCall(string Token, string CurrentPassword, string NewPassword, bool RevokeAccessTokens = true);

/// <summary>
/// 測試用的「我的帳號」。預設照真的規則的大意：目前的密碼對了就換，並登出同一個帳號的其他工作階段；
/// 測試也可以換掉 <see cref="OnChange"/>，直接指定身分模組回什麼。
/// </summary>
internal sealed class FakeAccountService(FakeAuthenticationService accounts, FakeSessionService sessions) : IAccountService
{
    private readonly ConcurrentQueue<ChangePasswordCall> _calls = new();

    public IReadOnlyList<ChangePasswordCall> Calls => _calls.ToArray();

    public Func<ChangePasswordCall, Result<PasswordChanged>>? OnChange { get; set; }

    public Task<Result<PasswordChanged>> ChangeOwnPasswordAsync(
        string sessionToken, string currentPassword, string newPassword, bool revokeAccessTokens, CancellationToken cancellationToken)
    {
        var call = new ChangePasswordCall(sessionToken, currentPassword, newPassword, revokeAccessTokens);
        _calls.Enqueue(call);
        return Task.FromResult(OnChange is { } respond ? respond(call) : Change(call));
    }

    private Result<PasswordChanged> Change(ChangePasswordCall call)
    {
        if (sessions.Peek(call.Token) is not { } session || !accounts.HasPassword(session.User.Email, call.CurrentPassword))
        {
            return new Failure(FailureKind.Invalid, IdentityErrorCodes.PasswordIncorrect, "目前的密碼不正確。");
        }

        accounts.SetPassword(session.User.Email, call.NewPassword);
        var rotated = sessions.Rotate(call.Token);
        if (rotated.Failure is not null || sessions.ActiveSession(rotated.Value.Session.Token) is not { } fresh)
        {
            return FakeSessionService.SessionEnded;
        }

        // 改密碼等於重新登入：期限從現在重新算。
        var rotation = rotated.Value;
        sessions.End(rotation.Session.Token);
        var renewed = sessions.Replace(fresh, DateTimeOffset.UtcNow.AddDays(IdentityRules.SessionAbsoluteDays));
        return new PasswordChanged(rotation.SignedOutSessions, 0, renewed);
    }
}
