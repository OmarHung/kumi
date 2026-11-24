using System.Text;

namespace PmTool.Cli.Import.Monday;

/// <summary>對應檔（YAML）的讀與寫。格式見 <see cref="MondayMapping"/> 與 internal/HANDOFF-monday-import.md 第 5 節。</summary>
internal static class MondayMappingFile
{
    /// <summary>
    /// 讀對應檔。<see cref="MondayMapping.Dump"/> 的相對路徑以對應檔所在的資料夾為準，回傳時已經是絕對路徑。
    /// 結構錯誤（不認得的鍵、型別不對、日期格式）丟 <see cref="CliException"/>，訊息帶行號與欄位的路徑。
    /// </summary>
    public static MondayMapping Load(string path)
    {
        var full = Path.GetFullPath(path);
        string yaml;
        try
        {
            yaml = File.ReadAllText(full);
        }
        catch (IOException exception)
        {
            throw new CliException(ExitCodes.Error, $"讀不了對應檔 {path}：{exception.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            throw new CliException(ExitCodes.Error, $"沒有權限讀對應檔 {path}。");
        }

        return new MondayMappingParser(path).Parse(yaml, Path.GetDirectoryName(full)!);
    }

    /// <summary>
    /// 寫對應檔。值是空的（留白）的項目 Load 時會略過，所以寫了再讀回來，留白的標籤對應不會留下。
    /// <paramref name="annotations"/> 是附加的說明（plan 的草稿用）。
    /// </summary>
    public static void Save(string path, MondayMapping mapping, MappingAnnotations? annotations = null)
    {
        var text = new MondayMappingWriter(annotations ?? new MappingAnnotations()).Write(mapping);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
