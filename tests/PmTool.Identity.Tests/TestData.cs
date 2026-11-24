namespace PmTool.Identity.Tests;

/// <summary>測試用的假資料。不能出現真實的個人資料，Email 一律用 example.com。</summary>
internal static class TestData
{
    public const string Password = "example-password";

    public static readonly FirstOwner Owner = new("範例組織", "範例擁有者", "owner@example.com", Password);

    /// <summary>每次都不一樣的 Email：同一個資料庫裡的測試才不會撞到彼此的帳號。</summary>
    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>
    /// 同一個 Email 的兩種 Unicode 寫法，畫面上一模一樣：é 是一個字元（組合字），或 e 後面接重音符號（分解字）。
    /// 用字元碼寫出來，原始碼裡才看得出兩者的差別。
    /// </summary>
    public static (string Composed, string Decomposed) UniqueAccentedEmail()
    {
        var id = Guid.NewGuid().ToString("N");
        return ($"jos{(char)0x00E9}-{id}@example.com", $"jose{(char)0x0301}-{id}@example.com");
    }

    /// <summary>不可能是合法 Email 的輸入。字元碼用 (char) 寫，測試名稱另外取，結果檔裡才不會出現控制字元。</summary>
    public static IEnumerable<TestCaseData> ImpossibleEmails()
    {
        yield return Email("a" + (char)0x0000 + "b@example.com", "Email 有 NUL");
        yield return Email("a" + (char)0x001B + "b@example.com", "Email 有其他控制字元");
        yield return Email("a" + (char)0xD800 + "b@example.com", "Email 有落單的代理字元");
        yield return Email(new string('a', IdentityRules.MaxEmailLength - 11) + "@example.com", "Email 超過上限");
        yield return Email("not-an-email", "Email 格式不對");
        yield return Email("a b@example.com", "Email 中間有空白");
        yield return Email("   ", "Email 只有空白");
    }

    private static TestCaseData Email(string email, string name) => new TestCaseData(email).SetArgDisplayNames(name);
}
