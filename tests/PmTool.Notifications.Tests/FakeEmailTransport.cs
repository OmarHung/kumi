using System.Collections.Concurrent;
using PmTool.Notifications.Email;

namespace PmTool.Notifications.Tests;

/// <summary>
/// 測試用的傳送：記下連線與寄出的信，也能模擬各種失敗。
/// 模組用 TryAdd 註冊正式的傳送，測試先放這個，就不會真的連出去。
/// </summary>
internal sealed class FakeEmailTransport : IEmailTransport
{
    private readonly ConcurrentQueue<OutgoingEmail> _sent = new();
    private readonly ConcurrentQueue<SmtpConnection> _connections = new();
    private int _closed;

    public IReadOnlyList<OutgoingEmail> Sent => _sent.ToArray();

    public IReadOnlyList<SmtpConnection> Connections => _connections.ToArray();

    /// <summary>寄給這個地址的信。</summary>
    public IReadOnlyList<OutgoingEmail> SentTo(string address) => [.. Sent.Where(email => email.ToAddress == address)];

    public int ClosedSessions => Volatile.Read(ref _closed);

    /// <summary>連線時回傳例外就失敗；null 是成功。</summary>
    public Func<SmtpConnection, EmailDeliveryException?> OnOpen { get; set; } = _ => null;

    /// <summary>寄信時回傳例外就失敗（這封信不算寄出）；null 是成功。</summary>
    public Func<OutgoingEmail, EmailDeliveryException?> OnSend { get; set; } = _ => null;

    /// <summary>回傳 true 的信寄的時候不回應，直到被取消；模擬一台卡住的郵件主機。</summary>
    public Func<OutgoingEmail, bool> HangOnSend { get; set; } = _ => false;

    /// <summary>設了之後，寄信會停在這裡（並通知 <see cref="Entered"/>）直到它完成；測試用來模擬「正在寄」。</summary>
    public TaskCompletionSource? Gate { get; set; }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IEmailSession> OpenAsync(SmtpConnection connection, CancellationToken cancellationToken)
    {
        _connections.Enqueue(connection);
        return OnOpen(connection) is { } failure ? Task.FromException<IEmailSession>(failure) : Task.FromResult<IEmailSession>(new Session(this));
    }

    private sealed class Session(FakeEmailTransport owner) : IEmailSession
    {
        public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
        {
            owner.Entered.TrySetResult();
            if (owner.HangOnSend(email))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (owner.Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            if (owner.OnSend(email) is { } failure)
            {
                throw failure;
            }

            owner._sent.Enqueue(email);
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref owner._closed);
            return ValueTask.CompletedTask;
        }
    }
}
