using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PmTool.Api.Json;

/// <summary>
/// API 的 JSON 規則：屬性 camelCase、列舉是 snake_case 字串、時刻是 UTC。
/// 寫回應與讀請求用同一套命名，但讀請求另外嚴格：型別不對、有不認得或重複的屬性一律不接受。
/// </summary>
internal static class ApiJson
{
    /// <summary>讀請求內文用。和寫回應分開，回應裡某個值意外是 null 時不會變成 500。</summary>
    public static readonly JsonSerializerOptions RequestOptions = CreateRequestOptions();

    public static IServiceCollection AddApiJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options => ConfigureShared(options.SerializerOptions));

    private static void ConfigureShared(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new SnakeCaseEnumConverterFactory());
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        options.Converters.Add(new OptionalJsonConverterFactory());
    }

    private static JsonSerializerOptions CreateRequestOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            // Web 的預設值會接受大小寫不同的屬性名稱與字串形式的數字；合約寫的是 camelCase 與數字，照合約收。
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        ConfigureShared(options);
        options.MakeReadOnly();
        return options;
    }
}
