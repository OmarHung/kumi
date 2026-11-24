using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PmTool.Api.Json;

/// <summary>
/// ISO 8601 的時間，一定要帶時區（Z 或 ±hh:mm）：沒寫時區的時間不知道是哪裡的，不猜，
/// 更不能依伺服器的時區解讀（換一台主機結果就不同）。歷史匯入的時間（建立、留言、上傳）都用這個規則。
/// </summary>
internal static partial class ZonedTimestamp
{
    public const string Message = "時間要帶時區（Z 或 ±hh:mm），例如 2026-01-02T03:04:05Z。";

    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        return text is not null
            && Shape().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    [GeneratedRegex(@"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}

/// <summary>沒帶時區的時間，<see cref="JsonBody{T}"/> 認得它，回 400 並寫明原因。</summary>
internal sealed class ZonelessTimestampException() : JsonException(ZonedTimestamp.Message);

/// <summary>只收帶時區的時間（見 <see cref="ZonedTimestamp"/>）；用在請求裡的 <c>createdAt</c>、<c>at</c>。寫回應不用它。</summary>
internal sealed class ZonedDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && ZonedTimestamp.TryParse(reader.GetString(), out var value)
            ? value
            : throw new ZonelessTimestampException();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime);
}
