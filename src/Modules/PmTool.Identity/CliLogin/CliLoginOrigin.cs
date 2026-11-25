namespace PmTool.Identity.CliLogin;

/// <summary>來源鍵的格式檢查：核准與兌換都用同一份，空白或超過欄位長度的一律不算有來源。</summary>
internal static class CliLoginOrigin
{
    public static bool IsValid(string? origin) =>
        !string.IsNullOrWhiteSpace(origin)
        && origin.Length <= CliLoginRules.MaxOriginLength
        && !origin.Any(char.IsControl);
}
