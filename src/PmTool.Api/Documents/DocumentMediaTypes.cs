using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using PmTool.Api.Responses;
using PmTool.Blueprint;

namespace PmTool.Api.Documents;

internal static class DocumentMediaTypes
{
    private static readonly FrozenDictionary<string, DocumentFormat> Formats =
        new Dictionary<string, DocumentFormat>
        {
            ["application/json"] = DocumentFormat.Json,
            ["application/yaml"] = DocumentFormat.Yaml,
            ["application/x-yaml"] = DocumentFormat.Yaml,
            ["text/yaml"] = DocumentFormat.Yaml,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // 內文一律用 UTF-8 解碼。宣告了別的編碼卻照 UTF-8 解，中文會默默變成亂碼再進驗證，不如明確拒絕。
    private static readonly string[] Utf8CharsetNames = ["utf-8", "utf8"];

    public static bool TryResolve(
        string? contentType,
        out DocumentFormat format,
        [NotNullWhen(false)] out ApiFailure? failure)
    {
        format = default;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
            || mediaType.MediaType.Value is not { } name
            || !Formats.TryGetValue(name, out format))
        {
            failure = ApiFailure.UnsupportedMediaType;
            return false;
        }

        failure = IsUtf8OrUnspecified(mediaType.Charset) ? null : ApiFailure.UnsupportedCharset;
        return failure is null;
    }

    /// <summary>登入、專案、項目這些端點只收 JSON，規則和文件端點一樣：媒體類型要對、編碼只能是 UTF-8。</summary>
    public static bool IsJson(string? contentType, [NotNullWhen(false)] out ApiFailure? failure)
    {
        if (!TryResolve(contentType, out var format, out failure))
        {
            failure = failure == ApiFailure.UnsupportedMediaType ? ApiFailure.JsonMediaTypeRequired : failure;
            return false;
        }

        failure = format == DocumentFormat.Json ? null : ApiFailure.JsonMediaTypeRequired;
        return failure is null;
    }

    private static bool IsUtf8OrUnspecified(StringSegment charset)
    {
        var name = HeaderUtilities.RemoveQuotes(charset);
        return name.Length == 0
            || Utf8CharsetNames.Any(utf8 => name.Equals(utf8, StringComparison.OrdinalIgnoreCase));
    }
}
