namespace PmTool.Cli.Import.Monday;

/// <summary>dump 資料夾的整份替換：先寫到暫存資料夾，成功才換上，中途失敗不會留下半份。</summary>
internal static class MondayDumpDirectory
{
    public static string NewTemporary(string target) => $"{target}.fetching-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>
    /// 輸出資料夾已經有東西、又不是 dump 時拒絕：替換的時候會把舊的整個刪掉，
    /// 打錯路徑（例如家目錄）就是刪掉別人的檔案。
    /// </summary>
    public static void EnsureReplaceable(string target)
    {
        if (File.Exists(target))
        {
            throw new CliException(ExitCodes.Invalid, $"--out 指到的是一個檔案：{target}。要給資料夾。");
        }

        if (Directory.Exists(target)
            && !File.Exists(MondayDumpLayout.MetaPath(target))
            && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new CliException(ExitCodes.Invalid, $"{target} 已經有東西，而且不是 kumi 抓的 dump（沒有 {MondayDumpLayout.MetaFile}）。為了不誤刪，請換一個空的資料夾。");
        }
    }

    public static void Replace(string temporary, string target)
    {
        if (!Directory.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(temporary, target);
            return;
        }

        var old = $"{target}.replaced-{Guid.NewGuid():N}";
        Directory.Move(target, old);
        try
        {
            Directory.Move(temporary, target);
        }
        catch
        {
            Directory.Move(old, target);
            throw;
        }

        KeepDownloadedFiles(old, target);
        Directory.Delete(old, recursive: true);
    }

    /// <summary>這次沒有下載檔案（沒給 --files）時，不要把上次下載的丟掉：它們依資產編號存放，留著不會錯。</summary>
    private static void KeepDownloadedFiles(string old, string target)
    {
        var previous = MondayDumpLayout.FilesPath(old);
        if (Directory.Exists(previous) && !Directory.Exists(MondayDumpLayout.FilesPath(target)))
        {
            Directory.Move(previous, MondayDumpLayout.FilesPath(target));
        }
    }
}
