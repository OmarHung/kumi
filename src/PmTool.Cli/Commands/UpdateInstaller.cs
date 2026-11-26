using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using PmTool.Cli.Api;

namespace PmTool.Cli.Commands;

/// <summary>
/// 把新版的執行檔下載到目標旁邊的暫存檔，核對、試跑之後才換掉目標。
/// 暫存檔放在同一個資料夾：換掉用的是同一個磁碟上的改名，不會有換到一半的檔案。
/// </summary>
internal static partial class UpdateInstaller
{
    private const int MoveAttempts = 5;

    private const int BufferSize = 81920;
    private const long MaxPackageBytes = 256L * 1024 * 1024;

    /// <summary>回傳換掉的執行檔路徑（符號連結已經解開）。</summary>
    public static async Task<string> InstallAsync(
        CommandCall call, UpdateEnvironment environment, string platform, JsonElement package, string version, CancellationToken cancellationToken)
    {
        var expected = ExpectedPackage.From(package);
        var target = ResolveTarget(environment);
        CleanUpLeftovers(target, environment);
        var temp = TempPath(target, environment);
        try
        {
            using var download = await call.Api.OpenDownloadAsync($"cli/download/{platform}", cancellationToken);
            var hash = await WriteAsync(call.Api.Server, download.Content, temp, expected.SizeBytes, environment.DownloadIdleTimeout, cancellationToken);
            if (!string.Equals(hash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CliException(ExitCodes.Error, "下載的內容和伺服器記的 sha256 不一樣，沒有更新。請再試一次。");
            }

            await VerifyRunsAsync(environment, temp, version, cancellationToken);
            Replace(temp, target, environment);
            return target;
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }

    /// <summary>執行檔是符號連結（例如 brew 或自己連過去的）時，換掉的是最後指到的那個檔案，連結留著。</summary>
    private static string ResolveTarget(UpdateEnvironment environment)
    {
        var path = environment.ExecutablePath
            ?? throw new CliException(ExitCodes.Error, "找不到 kumi 自己的執行檔位置，沒有更新。");
        var final = File.ResolveLinkTarget(path, returnFinalTarget: true);
        return final?.FullName ?? Path.GetFullPath(path);
    }

    private static string TempPath(string target, UpdateEnvironment environment)
    {
        // Windows 靠副檔名判斷能不能執行，暫存檔也要是 .exe。
        var extension = environment.IsWindows ? ".exe" : string.Empty;
        return Path.Combine(Path.GetDirectoryName(target)!, $".kumi-update-{Guid.NewGuid():N}{extension}");
    }

    private static async Task<string> WriteAsync(string server, Stream content, string temp, long limit, TimeSpan idleTimeout, CancellationToken cancellationToken)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long written = 0;
        try
        {
            await using var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            var buffer = new byte[BufferSize];
            while (true)
            {
                var read = await ReadNetworkAsync(server, content, buffer, idleTimeout, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                written += read;
                if (written > limit)
                {
                    throw new CliException(ExitCodes.Error, "下載的內容比伺服器說的大，已停止，沒有更新。");
                }

                sha.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new CliException(ExitCodes.Error,
                $"沒有權限寫入 {Path.GetDirectoryName(temp)}，沒有更新。請用有權限的帳號執行，或把 kumi 放在自己的資料夾（例如 ~/.local/bin）。");
        }
        catch (IOException exception)
        {
            throw new CliException(ExitCodes.Error, $"寫不進 {temp}（{exception.GetType().Name}），沒有更新。");
        }

        if (written != limit)
        {
            throw new CliException(ExitCodes.Error, "下載的大小和伺服器說的不一樣，沒有更新。請再試一次。");
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static async Task<int> ReadNetworkAsync(
        string server, Stream content, byte[] buffer, TimeSpan idleTimeout, CancellationToken cancellationToken)
    {
        // 每一次讀都重新計時：只管「多久沒有資料」，不限整個下載要多久。
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(idleTimeout);
        try
        {
            return await content.ReadAsync(buffer, idle.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliException(ExitCodes.Error,
                $"已經 {idleTimeout.TotalSeconds:0.#} 秒沒有收到伺服器 {server} 的資料，沒有更新。請檢查網路後再試一次。");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw new CliException(ExitCodes.Error, $"連不上伺服器 {server}：{exception.Message}");
        }
    }

    private static async Task VerifyRunsAsync(UpdateEnvironment environment, string temp, string version, CancellationToken cancellationToken)
    {
        // 第二個條件讓分析器知道這裡不會在 Windows 上執行；測試裡假裝的 Windows 照樣略過。
        if (!environment.IsWindows && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        var reported = await environment.SmokeRun(temp, cancellationToken);
        if (reported != $"kumi {version}")
        {
            throw new CliException(ExitCodes.Error, $"下載的檔案跑不起來，或回報的版本不是 {version}，沒有更新，原本的 kumi 沒有動。");
        }
    }

    private static void Replace(string temp, string target, UpdateEnvironment environment)
    {
        try
        {
            if (environment.IsWindows)
            {
                ReplaceOnWindows(temp, target, environment.MoveRetryDelay);
            }
            else
            {
                // 用 rename 換：複製覆蓋的話，macOS 會因為簽章的快取把程序殺掉。
                File.Move(temp, target, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliException(ExitCodes.Error, $"換不掉 {target}（{exception.GetType().Name}），沒有更新，原本的 kumi 還在。");
        }
    }

    /// <summary>執行中的 exe 不能覆蓋但可以改名：舊的先改成 .old，新的改成正式名字；第二步失敗就把舊的改回來。</summary>
    private static void ReplaceOnWindows(string temp, string target, TimeSpan retryDelay)
    {
        var old = $"{target}.old";
        try
        {
            File.Delete(old);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 上一次留下的 .old 還在執行中，刪不掉：換一個不會撞名的。
            old = $"{target}.{Guid.NewGuid():N}.old";
        }

        MoveWithRetry(target, old, retryDelay);
        try
        {
            MoveWithRetry(temp, target, retryDelay);
        }
        catch
        {
            MoveWithRetry(old, target, retryDelay);
            throw;
        }
    }

    /// <summary>防毒軟體剛掃完新檔時可能還佔著它，改名會暫時失敗；短暫重試幾次，最後一次的錯誤才往外丟。</summary>
    private static void MoveWithRetry(string source, string destination, TimeSpan retryDelay)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < MoveAttempts)
            {
                Thread.Sleep(retryDelay);
            }
        }
    }

    [GeneratedRegex("^\\.kumi-update-[0-9a-fA-F]{32}(\\.exe)?$")]
    private static partial Regex TempNamePattern();

    /// <summary>上次中斷留下的東西：暫存檔每個平台都有；Windows 另外有換下來的 .old。只認精確的名稱，不碰 kumi.exe.gold 這類別的檔案。</summary>
    private static void CleanUpLeftovers(string target, UpdateEnvironment environment)
    {
        var directory = Path.GetDirectoryName(target)!;
        var oldPattern = new Regex($"^{Regex.Escape(Path.GetFileName(target))}(\\.[0-9a-fA-F]{{32}})?\\.old$");
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 列不出來就不清理，不讓它變成整個更新的失敗。
            return;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var isTemp = TempNamePattern().IsMatch(name) && name.EndsWith(".exe", StringComparison.Ordinal) == environment.IsWindows;
            if (isTemp || (environment.IsWindows && oldPattern.IsMatch(name)))
            {
                DeleteQuietly(file);
            }
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 刪不掉不蓋過真正的結果；暫存檔是隱藏檔，下次更新不受影響。
        }
    }

    private sealed record ExpectedPackage(string Sha256, long SizeBytes)
    {
        public static ExpectedPackage From(JsonElement package)
        {
            var sha = package.Text("sha256")?.ToLowerInvariant();
            var size = package.Number("sizeBytes");
            if (sha is not { Length: 64 } || !sha.All(Uri.IsHexDigit) || size is not > 0 or > MaxPackageBytes)
            {
                throw new CliException(ExitCodes.Error, "伺服器回的下載資訊不完整或大小不合理，沒有更新。");
            }

            return new ExpectedPackage(sha, size.Value);
        }
    }
}
