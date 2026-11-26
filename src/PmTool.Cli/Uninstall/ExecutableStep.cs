namespace PmTool.Cli.Uninstall;

/// <summary>kumi 自己的執行檔：macOS 刪掉；Windows 刪不掉執行中的檔案、dotnet tool 裝的不歸 kumi 管，只說明怎麼做。</summary>
internal static class ExecutableStep
{
    public static async Task RunAsync(UninstallSession session)
    {
        const string name = UninstallSession.Executable;
        await session.HeadingAsync("kumi 執行檔");
        if (session.IsSkipped(name))
        {
            await session.RecordAsync(new(name, UninstallStatus.Skipped, ["略過（--skip executable）。"]));
            return;
        }

        var update = session.Context.Update;
        if (!update.IsStandalone)
        {
            await session.RecordAsync(new(name, UninstallStatus.Manual,
                ["這份 kumi 是用 dotnet tool 裝的，請執行 dotnet tool uninstall --global PmTool.Cli（裝在資料夾的改用 --tool-path 那個資料夾）。"]));
            return;
        }

        // 只刪名字是 kumi 的檔案：程序的路徑萬一是別的主程式，絕不能刪到它。
        if (update.ExecutablePath is not { } path || !IsKumi(path) || !File.Exists(path))
        {
            await session.RecordAsync(new(name, UninstallStatus.NotFound, ["找不到 kumi 執行檔的位置，請自己刪掉它。"]));
            return;
        }

        await session.RecordAsync(update.IsWindows ? await OnWindowsAsync(session, path) : await DeleteAsync(session, path));
    }

    /// <summary>正在執行的 .exe 在 Windows 上刪不掉，也不能請系統開機時刪（那要系統管理員）；kumi update 留下的舊檔沒在執行，問過可以刪。</summary>
    private static async Task<UninstallStep> OnWindowsAsync(UninstallSession session, string path)
    {
        var old = path + ".old";
        List<string> messages = [];
        if (File.Exists(old) && await session.ConfirmAsync($"刪掉 kumi update 留下的舊檔 {old}？", defaultYes: true))
        {
            messages.Add(LocalFileSteps.TryDelete(old) ?? $"已刪掉 {old}。");
        }

        messages.Add($"Windows 不能刪掉正在執行的檔案：kumi 結束之後請自己刪掉 {path}，並把 {Path.GetDirectoryName(path)} 從使用者的 PATH 拿掉。");
        return new(UninstallSession.Executable, UninstallStatus.Manual, messages);
    }

    private static async Task<UninstallStep> DeleteAsync(UninstallSession session, string path)
    {
        if (!await session.ConfirmAsync($"刪掉執行檔 {path}？", defaultYes: true))
        {
            return new(UninstallSession.Executable, UninstallStatus.Declined, ["沒有刪執行檔。"]);
        }

        if ((LocalFileSteps.TryDelete(path) ?? LocalFileSteps.TryDelete(path + ".old")) is { } failure)
        {
            return new(UninstallSession.Executable, UninstallStatus.Failed, [failure]);
        }

        session.Remind($"安裝時如果把 {Path.GetDirectoryName(path)} 加進了 shell 的 PATH（例如 ~/.zprofile），裡面沒有別的程式的話可以把那一行拿掉。");
        return new(UninstallSession.Executable, UninstallStatus.Done, [$"已刪掉 {path}。"]);
    }

    private static bool IsKumi(string path) =>
        Path.GetFileNameWithoutExtension(path).StartsWith("kumi", StringComparison.OrdinalIgnoreCase);
}
