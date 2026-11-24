using System.Text;
using System.Text.Json;

namespace PmTool.Ai.Providers;

/// <summary>
/// 串流回應的包裝。OpenRouter 在串流開始後才出錯時，HTTP 狀態已經是 200，只在串流裡送一則帶 <c>error</c> 的 data，
/// OpenAI 的 SDK 會把它當成一則空的更新，回覆就無聲無息地斷在一半。這裡逐行看 data，遇到 <c>error</c> 就在那一行之前停下來
/// （前面已經完整的更新照常交出去），下一次讀取丟 <see cref="AiProviderException"/>。其他的行（含心跳的註解行）原樣交出去。
/// </summary>
internal sealed class OpenRouterErrorStream(Stream inner) : Stream
{
    // 一行再長也不會有超過這個的 error；超過的不看，只是不抓錯，不會讓記憶體無限長。
    private const int MaxLineBytes = 256 * 1024;

    private readonly List<byte> _line = [];
    private bool _lineTooLong;
    private AiProviderException? _failure;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ThrowPending();
        var read = inner.Read(buffer);
        return Inspect(buffer[..read]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowPending();
        var read = await inner.ReadAsync(buffer, cancellationToken);
        return Inspect(buffer.Span[..read]);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ThrowPending()
    {
        if (_failure is not null)
        {
            throw _failure;
        }
    }

    /// <summary>回傳這一塊可以交出去的位元組數：找到 error 的那一行之前。找到時記下失敗，下一次讀取才丟（沒有可交的就馬上丟）。</summary>
    private int Inspect(Span<byte> chunk)
    {
        var lineStart = 0;
        for (var i = 0; i < chunk.Length; i++)
        {
            if (chunk[i] != (byte)'\n')
            {
                Append(chunk[i]);
                continue;
            }

            if (!_lineTooLong && ErrorOf(_line) is { } message)
            {
                // 這一行有一部分可能已經在前一塊交出去了（lineStart 是 0 時整行都在這一塊裡或之前），那部分沒辦法收回，SDK 還在等行尾。
                _failure = new AiProviderException($"OpenRouter 在回覆到一半時回報錯誤：{message}");
                if (lineStart == 0)
                {
                    ThrowPending();
                }

                return lineStart;
            }

            _line.Clear();
            _lineTooLong = false;
            lineStart = i + 1;
        }

        return chunk.Length;
    }

    private void Append(byte value)
    {
        if (_lineTooLong)
        {
            return;
        }

        if (_line.Count >= MaxLineBytes)
        {
            _lineTooLong = true;
            _line.Clear();
            return;
        }

        _line.Add(value);
    }

    private static string? ErrorOf(List<byte> line)
    {
        // 先用便宜的比對擋掉絕大多數的行，不必每一行都解析 JSON。
        if (line.Count < "data:".Length || line[0] != (byte)'d')
        {
            return null;
        }

        var text = Encoding.UTF8.GetString([.. line]).TrimEnd('\r');
        if (!text.StartsWith("data:", StringComparison.Ordinal) || !text.Contains("\"error\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text["data:".Length..].Trim());
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind is JsonValueKind.Null)
            {
                return null;
            }

            return error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                    ? message.GetString()
                    : "未說明原因。";
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
