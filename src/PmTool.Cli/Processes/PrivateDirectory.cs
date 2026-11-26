namespace PmTool.Cli.Processes;

/// <summary>
/// 引擎的工作目錄：每次新建、只有自己進得去（Unix 上權限 700）的空資料夾。不用共用的暫存資料夾本身：
/// Linux 的 <c>/tmp</c> 誰都寫得進去，別人放一個 <c>.mcp.json</c> 或專案設定，引擎就會讀進去。
/// </summary>
internal static class PrivateDirectory
{
    public static DirectoryInfo Create(string prefix)
    {
        var directory = Directory.CreateTempSubdirectory(prefix);
        if (!OperatingSystem.IsWindows())
        {
            // CreateTempSubdirectory 在 Unix 上本來就是 700；明確再設一次，不靠平台的預設。
            File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return directory;
    }

    public static void DeleteQuietly(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 刪不掉就算了：裡面沒有機密，系統的暫存資料夾也會清。
        }
    }
}
