using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PmTool.Api.Json;

/// <summary>列舉在 API 上的名字：snake_case 的字串（in_progress、not_started）。JSON 與查詢參數共用這一份。</summary>
internal static class SnakeCaseEnums
{
    public static string NameOf<TEnum>(TEnum value)
        where TEnum : struct, Enum => Names<TEnum>.ByValue[value];

    public static bool TryParse<TEnum>(string? name, out TEnum value)
        where TEnum : struct, Enum => Names<TEnum>.ByName.TryGetValue(name ?? string.Empty, out value);

    public static IEnumerable<string> AllNames<TEnum>()
        where TEnum : struct, Enum => Names<TEnum>.ByName.Keys;

    private static class Names<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly FrozenDictionary<string, TEnum> ByName = Enum.GetValues<TEnum>()
            .ToFrozenDictionary(value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()), StringComparer.Ordinal);

        public static readonly FrozenDictionary<TEnum, string> ByValue =
            ByName.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);
    }
}

/// <summary>
/// 只認 snake_case 的名字。框架內建的轉換器認不得時會退回不分大小寫地比對 C# 的名字（Admin、NotStarted），
/// 用戶端就可能依賴合約外的寫法；數字也不收，否則 API 會綁死在列舉的順序上。
/// </summary>
internal sealed class SnakeCaseEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(SnakeCaseEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class SnakeCaseEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && SnakeCaseEnums.TryParse<TEnum>(reader.GetString(), out var value)
                ? value
                : throw new JsonException($"不認得的 {typeof(TEnum).Name} 值。");

        // 沒有定義的值是程式寫錯了，寫成數字會讓用戶端看到合約外的東西，所以讓它失敗。
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Enum.IsDefined(value)
                ? SnakeCaseEnums.NameOf(value)
                : throw new JsonException($"{typeof(TEnum).Name} 沒有定義 {value} 這個值。"));
    }
}
