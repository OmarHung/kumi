namespace PmTool.Blueprint.Documents;

/// <summary>
/// 讀檔階段的問題。這時候文件還沒變成樹，指不到節點，所以路徑一律是根（空字串）。
/// 訊息只帶行號與上限，不帶文件裡的文字：解析器自己的錯誤訊息常會引用原文，原樣轉出去等於把文件回送給呼叫端。
/// </summary>
internal static class DocumentIssues
{
    public static ValidationIssue Empty() => Create(IssueCodes.DocumentEmpty, "文件是空的。");

    public static ValidationIssue NoYamlDocument() =>
        Create(IssueCodes.DocumentEmpty, "YAML 裡沒有任何文件，只有註解或空白。");

    public static ValidationIssue TooLarge(int length) =>
        Create(IssueCodes.DocumentTooLarge, $"文件有 {length} 個字元，超過 {DocumentLimits.MaxCharacters} 個字元的上限。");

    public static ValidationIssue TooDeep() =>
        Create(IssueCodes.DocumentTooDeep, $"物件與陣列的巢狀超過 {DocumentLimits.MaxDepth} 層的上限。");

    public static ValidationIssue TooManyNodes() =>
        Create(IssueCodes.DocumentTooManyNodes, $"文件的值超過 {DocumentLimits.MaxNodes} 個的上限。");

    public static ValidationIssue UnpairedSurrogate() =>
        Create(IssueCodes.DocumentSyntax, "文件含有無效的 Unicode 字元（落單的代理字元）。");

    public static ValidationIssue JsonSyntax(long? zeroBasedLine) =>
        Create(IssueCodes.DocumentSyntax, zeroBasedLine is { } line ? $"JSON 語法錯誤，在第 {line + 1} 行。" : "JSON 語法錯誤。");

    public static ValidationIssue JsonInvalidEscape(long line) =>
        Create(IssueCodes.DocumentSyntax, $"JSON 第 {line} 行的字串用 \\u 跳脫寫出了落單的代理字元。");

    public static ValidationIssue JsonDuplicateKey() =>
        Create(IssueCodes.DocumentSyntax, "JSON 有重複的鍵；同一個物件裡的鍵不能重複。");

    public static ValidationIssue YamlSyntax(long line, long column) =>
        Create(IssueCodes.DocumentSyntax, line > 0 ? $"YAML 語法錯誤，在第 {line} 行第 {column} 欄。" : "YAML 語法錯誤。");

    public static ValidationIssue YamlUnreadable() =>
        Create(IssueCodes.DocumentSyntax, "YAML 語法錯誤，解析器無法讀取這份文件。");

    public static ValidationIssue YamlKeyNotScalar(long line) =>
        Create(IssueCodes.DocumentSyntax, $"YAML 第 {line} 行的鍵是 mapping 或 sequence；鍵只能是純量。");

    public static ValidationIssue YamlDuplicateKey(long line) =>
        Create(IssueCodes.DocumentSyntax, $"YAML 第 {line} 行的鍵和同一個 mapping 裡前面的鍵重複（以轉成 JSON 之後的名稱比對，1 和 \"1\" 算同一個）。");

    public static ValidationIssue YamlTagNotSupported(long line) =>
        Create(IssueCodes.DocumentSyntax, $"YAML 第 {line} 行用了標籤（例如 !!str 或 !custom）；不支援標籤，要字串請改用引號。");

    public static ValidationIssue YamlMultipleDocuments(long line) =>
        Create(IssueCodes.DocumentMultiple, $"YAML 第 {line} 行開始了第二份文件；一次只能送一份。");

    public static ValidationIssue YamlAliasNotAllowed(long line) =>
        Create(IssueCodes.DocumentAliasNotAllowed, $"YAML 第 {line} 行用了 anchor（&）或 alias（*）；不支援，請把內容直接寫出來。");

    private static ValidationIssue Create(string code, string message) => new(code, string.Empty, message);
}
