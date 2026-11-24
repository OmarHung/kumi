using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Unicode;
using PmTool.Api.Responses;

namespace PmTool.Api.Documents;

internal sealed record BodyReadResult(string Text, ApiFailure? Failure)
{
    public static BodyReadResult Ok(string text) => new(text, null);

    public static BodyReadResult Rejected(ApiFailure failure) => new(string.Empty, failure);
}

/// <summary>
/// 自己限制讀取量，不靠伺服器的 MaxRequestBodySize：TestServer 不套用那個設定，chunked 上傳也沒有 Content-Length 可以先擋。
/// </summary>
internal static class Utf8BodyReader
{
    private const int InitialBufferBytes = 16 * 1024;

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// 藍圖與變更集的文件。兩個驗證端點與送出變更集都走這裡，文件的上限只有一個地方決定，
    /// 驗證過的文件送出時不會因為上限不同而被擋下。
    /// </summary>
    public static Task<BodyReadResult> ReadDocumentAsync(
        HttpRequest request, TimeSpan timeout, CancellationToken cancellationToken) =>
        ReadAsync(request, ApiLimits.MaxDocumentBytes, timeout, cancellationToken);

    public static async Task<BodyReadResult> ReadAsync(
        HttpRequest request, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBytes)
        {
            return BodyReadResult.Rejected(ApiFailure.PayloadLargerThan(maxBytes));
        }

        // 期限自己算，不靠 Kestrel 的最低速率：那是算平均的，先快送再停住的內文可以拖一個多小時，
        // 這段時間已經收到的部分會一直留在記憶體裡。
        // 期限到了用 CancelPendingRead 叫醒正在等的讀取，不用取消權杖：權杖會讓讀取丟例外，
        // Kestrel 的內文讀取器就停在「讀到一半」的狀態，請求結束時它清不掉剩下的內文，每次都會記一筆錯誤。
        var reader = request.BodyReader;
        using var deadline = new CancellationTokenSource(timeout);
        using var wakeUp = deadline.Token.Register(static state => ((PipeReader)state!).CancelPendingRead(), reader);

        Received received;
        try
        {
            received = await ReadAtMostAsync(reader, maxBytes + 1, cancellationToken);
        }
        catch (BadHttpRequestException exception)
        {
            // Kestrel 發現內文的分塊格式錯誤、送得太慢或超過它自己的上限時會丟這個，屬於用戶端的錯，不該變成 500。
            return BodyReadResult.Rejected(FromServerRejection(exception.StatusCode, maxBytes));
        }

        if (received.TimedOut)
        {
            return BodyReadResult.Rejected(ApiFailure.RequestTimeout);
        }

        return received.Length > maxBytes
            ? BodyReadResult.Rejected(ApiFailure.PayloadLargerThan(maxBytes))
            : Decode(received.Buffer.AsSpan(0, received.Length));
    }

    private static ApiFailure FromServerRejection(int statusCode, int maxBytes) => statusCode switch
    {
        StatusCodes.Status413PayloadTooLarge => ApiFailure.PayloadLargerThan(maxBytes),
        StatusCodes.Status408RequestTimeout => ApiFailure.RequestTimeout,
        _ => ApiFailure.BadRequest,
    };

    /// <summary>讀到 <paramref name="limit"/> 個位元組就停，不管後面還有多少。上限加一個位元組就足以判斷超過。</summary>
    private static async Task<Received> ReadAtMostAsync(PipeReader reader, int limit, CancellationToken cancellationToken)
    {
        // 緩衝區跟著實際收到的量長大，不看 Content-Length：那是用戶端說的，照它一次配置整個上限，
        // 只送標頭就停住的連線也能讓伺服器先保留 1 MiB。
        var buffer = new byte[Math.Min(InitialBufferBytes, limit)];
        var length = 0;
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken);
            var available = result.Buffer;
            if (result.IsCanceled)
            {
                // 被期限叫醒的。讀取器一定要先歸位，請求結束時 Kestrel 才能把沒讀完的內文清掉。
                reader.AdvanceTo(available.Start);
                return new Received(buffer, length, TimedOut: true);
            }

            var take = (int)Math.Min(available.Length, limit - length);
            if (length + take > buffer.Length)
            {
                Array.Resize(ref buffer, (int)Math.Min(Math.Max((long)buffer.Length * 2, length + take), limit));
            }

            available.Slice(0, take).CopyTo(buffer.AsSpan(length));
            length += take;
            reader.AdvanceTo(available.GetPosition(take));

            if (length == limit || result.IsCompleted)
            {
                return new Received(buffer, length, TimedOut: false);
            }
        }
    }

    private static BodyReadResult Decode(ReadOnlySpan<byte> bytes)
    {
        var content = bytes.StartsWith(ByteOrderMark) ? bytes[ByteOrderMark.Length..] : bytes;

        // 先檢查再解碼：預設的解碼會把壞掉的位元組換成替代字元，文件內容就被悄悄改掉了。
        return Utf8.IsValid(content)
            ? BodyReadResult.Ok(Encoding.UTF8.GetString(content))
            : BodyReadResult.Rejected(ApiFailure.InvalidEncoding);
    }

    private readonly record struct Received(byte[] Buffer, int Length, bool TimedOut);
}
