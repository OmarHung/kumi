using System.Text.Json;
using System.Text.Json.Serialization;

namespace PmTool.Notifications.Persistence;

/// <summary>
/// 帳號通知存在 <c>account_notice</c> 欄位的 JSON。欄位與列舉用 snake_case，和 API 上的寫法一致；
/// 讀的時候容忍沒有的欄位（之後加欄位，舊的列照樣讀得出來）。
/// </summary>
internal static class AccountNoticeJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public static string Write(AccountNotice notice) => JsonSerializer.Serialize(notice, Options);

    public static AccountNotice Read(string json) =>
        JsonSerializer.Deserialize<AccountNotice>(json, Options) ?? throw new JsonException("帳號通知的內容是空的。");
}
