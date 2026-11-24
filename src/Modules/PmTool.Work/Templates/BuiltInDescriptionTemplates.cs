namespace PmTool.Work.Templates;

/// <summary>內嵌在組件裡的說明格式（Templates/Descriptions/*.md）。</summary>
internal static class BuiltInDescriptionTemplates
{
    private const string ResourcePrefix = "PmTool.Work.Templates.Descriptions.";

    private const string WebProjectId = "web_project";
    internal const string WebProjectName = "網站案子";

    /// <summary>每次都讀同一份內嵌資源；內容很小，不必另外快取。</summary>
    public static DescriptionTemplate WebProject(bool isDefault) =>
        new(WebProjectId, WebProjectName, Read(WebProjectId), IsBuiltIn: true, isDefault);

    private static string Read(string id)
    {
        var name = $"{ResourcePrefix}{id}.md";
        using var stream = typeof(BuiltInDescriptionTemplates).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"讀不到內嵌的說明格式 {name}，請確認 Templates/Descriptions/*.md 有內嵌進去。");
        using var reader = new StreamReader(stream);
        // 檔案在不同系統上的換行不同；統一成 \n，和存進資料庫的說明一致。
        return Common.TextRules.NormalizeLineBreaks(reader.ReadToEnd())!;
    }
}
