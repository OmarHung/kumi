using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Notifications;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的寄信設定：記住每次呼叫的參數。預設把存進來的東西原樣讀回去（密碼只說有沒有）。</summary>
internal sealed class FakeEmailSettingsService : IEmailSettingsService
{
    private readonly ConcurrentQueue<(Actor Actor, EmailSettingsInput Input)> _saveCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, EmailSettingsInput Input, string Recipient)> _testCalls = new();
    private readonly ConcurrentQueue<Actor> _getCalls = new();

    public Func<Actor, Result<EmailSettings>> OnGet { get; set; } = _ => Stored(
        new EmailSettingsInput(true, EmailProvider.Gmail, null, null, null, "team@example.com", "stored-secret", null, "Example 公司", "https://pm.example.com"));

    public Func<Actor, EmailSettingsInput, Result<EmailSettings>> OnSave { get; set; } = (_, input) => Stored(input);

    public Func<Actor, EmailSettingsInput, string, Result<Unit>> OnSendTest { get; set; } = (_, _, _) => Unit.Value;

    public IReadOnlyList<Actor> GetCalls => _getCalls.ToArray();

    public IReadOnlyList<(Actor Actor, EmailSettingsInput Input)> SaveCalls => _saveCalls.ToArray();

    public IReadOnlyList<(Actor Actor, EmailSettingsInput Input, string Recipient)> TestCalls => _testCalls.ToArray();

    public static EmailSettings Stored(EmailSettingsInput input, Guid? updatedBy = null) => new(
        input.Enabled,
        input.Provider,
        input.Host,
        input.Port,
        input.Security,
        input.Username,
        !string.IsNullOrEmpty(input.Password),
        input.FromAddress,
        input.FromName,
        input.SiteUrl,
        new EmailDeliveryStatus(
            new DateTimeOffset(2026, 11, 21, 6, 5, 0, TimeSpan.Zero), null, null),
        new DateTimeOffset(2026, 11, 21, 6, 0, 0, TimeSpan.Zero),
        updatedBy ?? TestUsers.Owner.PersonId);

    public Task<Result<EmailSettings>> GetAsync(Actor actor, CancellationToken cancellationToken)
    {
        _getCalls.Enqueue(actor);
        return Task.FromResult(OnGet(actor));
    }

    public Task<Result<EmailSettings>> SaveAsync(Actor actor, EmailSettingsInput input, CancellationToken cancellationToken)
    {
        _saveCalls.Enqueue((actor, input));
        return Task.FromResult(OnSave(actor, input));
    }

    public Task<Result<Unit>> SendTestAsync(Actor actor, EmailSettingsInput input, string recipientAddress, CancellationToken cancellationToken)
    {
        _testCalls.Enqueue((actor, input, recipientAddress));
        return Task.FromResult(OnSendTest(actor, input, recipientAddress));
    }
}

internal sealed class FakeEmailPreferenceService : IEmailPreferenceService
{
    private readonly ConcurrentQueue<(Actor Actor, bool Enabled)> _setCalls = new();

    public Func<Actor, EmailPreference> OnGet { get; set; } = _ => new EmailPreference(true, false);

    public Func<Actor, bool, EmailPreference> OnSet { get; set; } = (_, enabled) => new EmailPreference(enabled, true);

    public IReadOnlyList<(Actor Actor, bool Enabled)> SetCalls => _setCalls.ToArray();

    public Task<EmailPreference> GetAsync(Actor actor, CancellationToken cancellationToken) => Task.FromResult(OnGet(actor));

    public Task<EmailPreference> SetAsync(Actor actor, bool enabled, CancellationToken cancellationToken)
    {
        _setCalls.Enqueue((actor, enabled));
        return Task.FromResult(OnSet(actor, enabled));
    }
}
