using System.Runtime.CompilerServices;

namespace PmTool.Work.Tests;

/// <summary>
/// 讀 repo 裡的檔案，例如 schemas 底下的官方範例。這個測試專案沒有把範例複製到輸出目錄，
/// 所以從這個原始檔的位置（編譯時記下的）往上找 repo 的根；找不到再從測試的執行目錄往上找。
/// </summary>
internal static class RepositoryFiles
{
    private const string Marker = "PmTool.slnx";

    public static string ReadAllText(string relativePath, [CallerFilePath] string sourceFile = "")
    {
        var root = FindRoot(Path.GetDirectoryName(sourceFile)) ?? FindRoot(TestContext.CurrentContext.TestDirectory)
            ?? throw new InvalidOperationException($"找不到 repo 的根目錄（{Marker}），讀不到 {relativePath}。");
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string? FindRoot(string? start)
    {
        for (var directory = start is null ? null : new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, Marker)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
