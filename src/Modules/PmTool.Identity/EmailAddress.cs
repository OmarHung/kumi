using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace PmTool.Identity;

internal static class EmailAddress
{
    /// <summary>
    /// 轉成存與查共用的寫法，不可能是合法 Email 時回 false。登入、新增人員、第一個擁有者都用這一個：
    /// 大小寫、頭尾空白、組合字或分解字這些畫面上分不出來的差別，才不會變成另一個帳號，或讓人登入不了。
    /// </summary>
    public static bool TryNormalize(string email, [NotNullWhen(true)] out string? normalized)
    {
        var trimmed = email.Trim();

        // 要在 Unicode 正規化之前擋：落單的代理字元會讓它丟例外。
        if (InputText.HasForbiddenCharacters(trimmed) || InputText.HasInvisibleCharacters(trimmed))
        {
            normalized = null;
            return false;
        }

        // 先相容正規化再轉小寫（全形、上標這類畫面上看起來等價的寫法會變成同一個）。
        // 反過來的話，大寫字母加符號的分解寫法（例如 I 加上一點）轉出來會和組合字不一樣。
        var candidate = trimmed.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        normalized = HasPlausibleShape(candidate) ? candidate : null;
        return normalized is not null;
    }

    /// <summary>
    /// 只擋明顯打錯的寫法。完整的規格允許很多實際上收不到信的形式，
    /// 而真正收不收得到只有寄信才知道，所以這裡不追求完全照規格。
    /// </summary>
    private static bool HasPlausibleShape(string email)
    {
        if (email.Length is 0 or > IdentityRules.MaxEmailLength || email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@'))
        {
            return false;
        }

        var domain = email[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.Contains("..");
    }
}
