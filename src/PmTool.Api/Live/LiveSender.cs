namespace PmTool.Api.Live;

/// <summary>即時推送的時間上限。測試換成很短的數字，才不必真的等。</summary>
public sealed record LiveOptions
{
    /// <summary>一次送出（一個群組、一個收件匣）最多等多久。</summary>
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// 送出推送的唯一入口。SignalR 對一個群組送出時會等每一條連線寫完：一條連線不讀資料（網路卡住、故意不讀）
/// 寫入就會一直卡住，後面所有的推送、通知的處理、請求的回應都跟著被拖住。所以每一次送出都有自己的期限，
/// 逾時或出錯只記 Warning、當作送完了，繼續下一個；那條卡住的連線由 SignalR 的逾時與心跳自己斷開。
/// 等待用 <see cref="Task.WaitAsync(CancellationToken)"/>：即使底層的送出不理會取消，這裡也一樣放手往下走。
/// </summary>
public sealed partial class LiveSender(LiveOptions options, ILogger<LiveSender> logger)
{
    /// <param name="what">記錄用的描述（推給誰、什麼訊息）。</param>
    /// <param name="cancellationToken">呼叫端自己的取消（程序關閉）；它取消時例外照常往外丟，不當成逾時。</param>
    public async Task SendAsync(string what, Func<CancellationToken, Task> send, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.SendTimeout);
        try
        {
            await send(deadline.Token).WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(what, options.SendTimeout.TotalSeconds);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception, what);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "推送「{What}」超過 {Seconds} 秒還沒送完，放棄這一次繼續下一個；可能有一條連線不讀資料。")]
    private partial void LogTimedOut(string what, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "推送「{What}」失敗，網頁要等下一次重抓才會更新。")]
    private partial void LogFailed(Exception exception, string what);
}
