using System.Text.Json;

namespace PmTool.Blueprint.Validation;

/// <summary>列舉在文件裡的寫法（例如 set_field）。和 ModelBinder 用同一個命名規則，訊息裡的名稱才對得上使用者寫的字。</summary>
internal static class DocumentNames
{
    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
