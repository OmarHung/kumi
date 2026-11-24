using System.Text.Json;
using System.Text.Json.Serialization;
using PmTool.Abstractions;

namespace PmTool.Api.Json;

/// <summary>時刻一律寫成 UTC 加上 Z。模組存的是 UTC，但 DateTimeOffset 預設會寫成 +00:00，用戶端還要自己處理時區。</summary>
internal sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime);
}

/// <summary>
/// 讓局部更新分得出「沒給」與「給了 null」：屬性沒出現時轉換器不會被呼叫，留下預設值（沒給）；
/// 出現時不管是不是 null 都包成有值的 <see cref="Optional{T}"/>。
/// </summary>
internal sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(OptionalJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        // null 也要交給 Read，才能變成「給了 null」；否則框架會直接放預設值，和沒給分不出來。
        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                return new(JsonSerializer.Deserialize<T>(ref reader, options)!);
            }
            catch (JsonException exception)
            {
                // 內層的呼叫只知道自己是根（$），換成沒有路徑的例外，外層才會補上真正的屬性位置。
                throw new JsonException(exception.Message, exception);
            }
        }

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
