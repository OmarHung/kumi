using System.Buffers;
using System.Text;

namespace PmTool.Identity;

internal static class InputText
{
    /// <summary>
    /// 有沒有存不進資料庫、或不該出現在名稱與 Email 裡的字元。NUL 存不進也查不了 PostgreSQL 的 text；
    /// 落單的代理字元轉不成 UTF-8，Unicode 正規化也會丟例外；其他控制字元（包含換行）只會造成顯示問題。
    /// </summary>
    public static bool HasForbiddenCharacters(string text)
    {
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out var rune, out var consumed) != OperationStatus.Done || Rune.IsControl(rune))
            {
                return true;
            }

            rest = rest[consumed..];
        }

        return false;
    }
}
