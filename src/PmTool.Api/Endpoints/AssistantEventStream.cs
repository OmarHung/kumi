using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using PmTool.Ai;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace PmTool.Api.Endpoints;

/// <summary>串流的設定。公開是因為測試要換成短的心跳間隔，不必真的等十五秒（和 <see cref="ValidationLoadLimits"/> 一樣的做法）。</summary>
public sealed record AssistantStreamSettings(TimeSpan PingInterval)
{
    /// <summary>
    /// nginx 預設 60 秒沒資料就斷線，思考深度高的時候模型可能安靜一分鐘以上；每 15 秒送一行註解當心跳。
    /// </summary>
    public static AssistantStreamSettings Default { get; } = new(TimeSpan.FromSeconds(15));
}

/// <summary>
/// 助理回覆的事件串流（text/event-stream）：每個事件 <c>event:</c> 一行、<c>data:</c> 一行 JSON、空一行，寫完就 flush；
/// 沒有事件時每隔一段時間寫一行 <c>: ping</c>。格式見 <c>internal/HANDOFF-ai-assistant.md</c> 第 6.2 節。
/// 用自己的 <see cref="IResult"/>：標頭與逐段送出都要自己掌握，不能讓任何一層把它緩衝起來。
/// </summary>
internal sealed class AssistantEventStream(AssistantRun run, AssistantStreamSettings settings) : IResult
{
    private const string PingLine = ": ping\n\n";

    public async Task ExecuteAsync(HttpContext http)
    {
        var response = http.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.Headers.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var json = http.RequestServices.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        var abort = http.RequestAborted;

        // 讀的人離開（取消、寫不出去）時，DisposeAsync 會叫回覆停下來、等它存完才回。
        await using var events = run.Events.GetAsyncEnumerator(abort);
        try
        {
            await WriteAsync(response, abort, "start", new AssistantStartEventData(AssistantMessageData.From(run.UserMessage), run.AssistantMessageId), json);
            await PumpAsync(response, events, json, abort);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // 用戶端關掉了連線：沒有人可以回，也沒有東西要補寫；回覆由上面的 DisposeAsync 收尾。
        }
    }

    private async Task PumpAsync(HttpResponse response, IAsyncEnumerator<AssistantRunEvent> events, JsonSerializerOptions json, CancellationToken abort)
    {
        var next = events.MoveNextAsync().AsTask();
        while (true)
        {
            try
            {
                if (!await next.WaitAsync(settings.PingInterval, abort))
                {
                    return;
                }
            }
            catch (TimeoutException)
            {
                await response.WriteAsync(PingLine, Encoding.UTF8, abort);
                await response.Body.FlushAsync(abort);
                continue;
            }

            await WriteEventAsync(response, events.Current, json, abort);
            next = events.MoveNextAsync().AsTask();
        }
    }

    private static Task WriteEventAsync(HttpResponse response, AssistantRunEvent runEvent, JsonSerializerOptions json, CancellationToken abort) =>
        runEvent switch
        {
            AssistantTextDelta text => WriteAsync(response, abort, "text", new AssistantTextEventData(text.Delta), json),

            // 用基底型別序列化，資料裡才有 type: "tool"。
            AssistantToolUpdate tool => WriteAsync<AssistantPartData>(response, abort, "tool", AssistantPartData.From(tool.Tool), json),
            AssistantRunQueued queued => WriteAsync(response, abort, "queued", new AssistantQueuedEventData(queued.Position), json),
            AssistantRunStarted => WriteAsync(response, abort, "started", new AssistantStartedEventData(), json),
            AssistantRunCompleted done => WriteAsync(response, abort, "done", new AssistantDoneEventData(AssistantMessageData.From(done.Message)), json),
            _ => throw new InvalidOperationException($"不認得的回覆事件：{runEvent.GetType().Name}。"),
        };

    private static async Task WriteAsync<T>(HttpResponse response, CancellationToken abort, string name, T data, JsonSerializerOptions json)
    {
        // JSON 不換行（沒有縮排），一個事件一行 data。
        var text = $"event: {name}\ndata: {JsonSerializer.Serialize(data, json)}\n\n";
        await response.WriteAsync(text, Encoding.UTF8, abort);
        await response.Body.FlushAsync(abort);
    }
}
