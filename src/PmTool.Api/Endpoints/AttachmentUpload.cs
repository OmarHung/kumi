using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using PmTool.Api.Responses;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 上傳請求的讀法。不用框架的表單繫結（<c>ReadFormAsync</c>）：它會把整個檔案先放進記憶體或暫存檔；
/// 這裡逐段讀 multipart，檔案那一段直接當成串流交給工作引擎，邊收邊寫進儲存空間。
/// </summary>
internal static class AttachmentUpload
{
    /// <summary>檔案以外的內容（boundary、欄位標頭）能多占的空間。</summary>
    public const long MultipartOverheadBytes = 1024 * 1024;

    public const long MaxRequestBytes = AttachmentRules.MaxBytes + MultipartOverheadBytes;

    public const string FileField = "file";

    private const int MaxBoundaryLength = 70;

    /// <summary>檔案前面的文字欄位（歷史匯入的來源、外部編號…）每個最多這麼長；都是短的值，超過是壞請求。</summary>
    private const int MaxFieldChars = 1024;

    public static readonly ApiFailure TooLarge = TooLargeWith(WorkErrorCodes.AttachmentTooLarge);

    /// <summary>項目與頁面的附件上限相同、錯誤代碼各自一個（代碼是對外合約）。</summary>
    public static ApiFailure TooLargeWith(string code) => ApiFailure.PayloadLargerThan(0) with
    {
        Code = code,
        Message = $"附件不能超過 {AttachmentRules.MaxBytes / (1024 * 1024)} MB。",
    };

    private static readonly ApiFailure NotMultipart = ApiFailure.UnsupportedMediaType with
    {
        Message = "上傳附件請用 multipart/form-data，檔案放在欄位 file。",
    };

    private static readonly ApiFailure UnknownField = ApiFailure.BadRequest with
    {
        Message = "請求裡有不認得的欄位，或欄位重複太多次。",
    };

    private static readonly ApiFailure NoFile = ApiFailure.BadRequest with
    {
        Message = "請求裡沒有檔案：檔案要放在 multipart/form-data 的欄位 file，而且要帶檔名。",
    };

    /// <summary>
    /// 放寬這個請求的大小上限：Kestrel 的 MaxRequestBodySize 只對這個端點調到檔案上限加 1 MiB，其他端點不受影響。
    /// 測試用的伺服器沒有這個功能，所以 <see cref="LimitedBodyStream"/> 與 Content-Length 的檢查才是真正擋人的那一層。
    /// </summary>
    public static void RaiseKestrelLimit(HttpContext context)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
        {
            feature.MaxRequestBodySize = MaxRequestBytes;
        }
    }

    /// <summary>
    /// 找出欄位 file 那一段。失敗時帶著要回給用戶端的錯誤；成功時 <see cref="OpenedFile.Content"/> 還沒有讀。
    /// <paramref name="fields"/> 不是 null 時，file 之前的文字欄位讀進去（檔案是串流，過了它就讀不到後面的欄位，所以欄位要放在 file 前面）；
    /// 這時 <paramref name="allowedFields"/> 是收的欄位名稱，別的名稱一律回 400、不存進 <paramref name="fields"/>，文字欄位的個數也不能超過它的個數。
    /// </summary>
    public static async Task<(OpenedFile? File, ApiFailure? Failure)> OpenAsync(
        HttpRequest request,
        CancellationToken cancellationToken,
        IDictionary<string, string>? fields = null,
        IReadOnlySet<string>? allowedFields = null,
        string tooLargeCode = WorkErrorCodes.AttachmentTooLarge)
    {
        var tooLarge = tooLargeCode == WorkErrorCodes.AttachmentTooLarge ? TooLarge : TooLargeWith(tooLargeCode);
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is not { Length: > 0 and <= MaxBoundaryLength } boundary)
        {
            return (null, NotMultipart);
        }

        // 超過上限的請求不必開始讀：Content-Length 是用戶端說的，說錯也沒關係，下面的串流會再算實際收到的量。
        if (request.ContentLength > MaxRequestBytes)
        {
            return (null, tooLarge);
        }

        var body = new LimitedBodyStream(request.Body, MaxRequestBytes);
        var reader = new MultipartReader(boundary, body);
        var textFields = 0;
        try
        {
            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                if (FileOf(section) is { } file)
                {
                    return (file, null);
                }

                if (fields is not null && await TextFieldOfAsync(section, cancellationToken) is { } field)
                {
                    if (allowedFields is not null && (!allowedFields.Contains(field.Name) || ++textFields > allowedFields.Count))
                    {
                        return (null, UnknownField);
                    }

                    fields[field.Name] = field.Value;
                }
            }
        }
        catch (RequestTooLargeException)
        {
            return (null, tooLarge);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or BadHttpRequestException or System.Text.DecoderFallbackException)
        {
            // multipart 的格式錯誤（缺 boundary、標頭太長）或連線中斷：用戶端的錯。
            return (null, exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } ? tooLarge : NoFile);
        }

        return (null, NoFile);
    }

    private static OpenedFile? FileOf(MultipartSection section)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            || !disposition.IsFileDisposition()
            || !string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, FileField, StringComparison.Ordinal))
        {
            return null;
        }

        // filename* 是 RFC 5987 的寫法，有的話以它為準；瀏覽器一般只送 filename（UTF-8 原文）。
        var name = disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName;
        return new OpenedFile(HeaderUtilities.RemoveQuotes(name).Value ?? string.Empty, section.ContentType, section.Body);
    }

    /// <summary>不是檔案的 form-data 欄位（沒有檔名）：名字與值。值太長或不是 UTF-8 時丟 <see cref="InvalidDataException"/>。</summary>
    private static async Task<(string Name, string Value)?> TextFieldOfAsync(MultipartSection section, CancellationToken cancellationToken)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            || !disposition.IsFormDisposition()
            || HeaderUtilities.RemoveQuotes(disposition.Name).Value is not { Length: > 0 } name)
        {
            return null;
        }

        using var reader = new StreamReader(section.Body, new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true), false, MaxFieldChars + 1, leaveOpen: true);
        var buffer = new char[MaxFieldChars + 1];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        return read > MaxFieldChars ? throw new InvalidDataException("欄位的值太長。") : (name, new string(buffer, 0, read));
    }

    /// <summary>檔名與類型是用戶端說的，工作引擎會整理與檢查。</summary>
    internal sealed record OpenedFile(string FileName, string? ContentType, Stream Content);

    /// <summary>實際收到超過上限就丟例外。Content-Length 可以不寫（chunked）或寫小，所以不能只看它。</summary>
    internal sealed class RequestTooLargeException() : Exception("請求超過上限。");
}
