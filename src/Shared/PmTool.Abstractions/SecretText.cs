using System.Text.RegularExpressions;

namespace PmTool.Abstractions;

/// <summary>
/// 私鑰與存取權杖的偵測：說明、頁面這類會被很多人讀到的文字，不讓人把它們貼進來。
/// 樣式和 scripts/precommit-scan.sh 裡的那一組同一份，改了任何一邊要同步另一邊。
/// 這只是防呆，不是防線：密碼字串猜不準，誤判的代價比漏掉高，所以只認格式固定的那幾種。
/// </summary>
public static partial class SecretText
{
    /// <summary>文字裡有沒有私鑰區塊（含 PGP 的 PRIVATE KEY BLOCK）、PuTTY 的檔頭，或已知格式的權杖；行內任何位置都算。</summary>
    public static bool ContainsSecret(string text) =>
        !string.IsNullOrEmpty(text)
        && (PrivateKeyRegex().IsMatch(text)
            || PuttyRegex().IsMatch(text)
            || KumiTokenRegex().IsMatch(text)
            || ProviderTokenRegex().IsMatch(text));

    // 空白放寬到 tab 與多個空白：頭尾的格式被改過一點，仍然是私鑰。
    [GeneratedRegex(@"-----BEGIN[ \t]+(?:[A-Z0-9]+[ \t]+)*PRIVATE[ \t]+KEY(?:[ \t]+BLOCK)?-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex("PuTTY-User-Key-File-[0-9]+:", RegexOptions.CultureInvariant)]
    private static partial Regex PuttyRegex();

    [GeneratedRegex(@"\b(?:pmt|pmr|kha|kjt)_[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant)]
    private static partial Regex KumiTokenRegex();

    [GeneratedRegex(
        @"sk-ant-(?:api|admin)\d\d-[A-Za-z0-9_-]{80,}"
        + @"|sk-or-v1-[0-9a-f]{64}"
        + @"|sk-proj-[A-Za-z0-9_-]{40,}|(?<![A-Za-z0-9_-])sk-(?!ant-|or-|proj-)[A-Za-z0-9]{40,}"
        + @"|AIza[0-9A-Za-z_-]{35}"
        + @"|\bgh[opsur]_[A-Za-z0-9]{36,}|\bgithub_pat_[A-Za-z0-9_]{30,}"
        + @"|\b(?:AKIA|ASIA)[0-9A-Z]{16}\b"
        + @"|\bxapp-[A-Za-z0-9-]{4,}|\bxox[baprs]-[A-Za-z0-9-]{4,}"
        + @"|\bglpat-[A-Za-z0-9_-]{20,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProviderTokenRegex();
}
