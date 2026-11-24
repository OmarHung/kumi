using System.Security.Cryptography;
using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>
/// 下載附件。串流寫到同資料夾的暫存檔，核對過才 rename 成正式的名字：
/// 中途失敗或內容不對時，不會留下半個檔案，也不會蓋掉原本的檔案。
/// </summary>
internal static class AttachmentDownload
{
    private const int BufferSize = 81920;

    public static async Task<int> RunAsync(CommandCall call)
    {
        var idText = call.Arguments.Positionals[0];
        if (!Guid.TryParse(idText, out var id))
        {
            throw CliException.Usage($"附件編號要是 GUID（用 kumi item attachments 看）：{idText}");
        }

        var expected = await FindListedAsync(call, id);
        using var download = await call.Api.OpenDownloadAsync($"attachments/{id}/content", call.CancellationToken);
        var serverName = expected?.Text("fileName") ?? download.FileName;
        var target = ResolveTarget(call, SafeName(serverName, id));
        var force = call.Arguments.Has("force");
        if (!force && (File.Exists(target) || Directory.Exists(target)))
        {
            throw CliException.Usage($"{target} 已經存在，沒有覆蓋。要蓋掉請加 --force。");
        }

        var expectedHash = await ChooseHashAsync(call, expected?.Text("sha256"), download.Sha256);
        var hash = await WriteVerifiedAsync(call, download, target, force, expectedHash);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, new System.Text.Json.Nodes.JsonObject { ["path"] = target, ["sha256"] = hash });
        }
        else
        {
            await call.Out.WriteLineAsync($"已下載到 {target}");
        }

        if (expectedHash is null)
        {
            await call.Error.WriteLineAsync("提醒：伺服器沒有提供 sha256，只核對了大小。");
        }

        return ExitCodes.Success;
    }

    /// <summary>列表的 sha256 是資料庫記的，優先；兩邊不一樣時照列表核對，並說明（內容最多符合其中一個）。</summary>
    private static async Task<string?> ChooseHashAsync(CommandCall call, string? listed, string? header)
    {
        if (listed is not null && header is not null && !string.Equals(listed, header, StringComparison.OrdinalIgnoreCase))
        {
            await call.Error.WriteLineAsync("提醒：下載回應的 sha256 和附件列表的不一樣，照列表的核對。");
        }

        return listed ?? header;
    }

    private static string SafeName(string? name, Guid id) => AttachmentFiles.SafeFileName(name, $"attachment-{id}");

    /// <summary>有給 --item 才讀列表（核對用的 sha256 與檔名以它為準）；附件不在那個項目裡就是找不到。</summary>
    private static async Task<System.Text.Json.JsonElement?> FindListedAsync(CommandCall call, Guid id)
    {
        if (call.Arguments.Value("item") is not { } itemKey)
        {
            return null;
        }

        var list = await call.Api.GetAsync($"items/{Uri.EscapeDataString(itemKey)}/attachments", null, call.CancellationToken);
        foreach (var item in list.Items())
        {
            if (Guid.TryParse(item.Text("id"), out var listed) && listed == id)
            {
                return item;
            }
        }

        throw new CliException(ExitCodes.Error, $"{itemKey} 沒有編號 {id} 的附件。");
    }

    /// <summary>-o 是資料夾就放在裡面；是檔案路徑就照它存（使用者自己給的，不整理）；沒給就是目前資料夾。</summary>
    private static string ResolveTarget(CommandCall call, string safeName)
    {
        var directory = call.Context.WorkingDirectory;
        if (call.Arguments.Value("output") is not { } output)
        {
            return Path.Combine(directory, safeName);
        }

        var full = Path.GetFullPath(output, directory);
        if (Directory.Exists(full))
        {
            return Path.Combine(full, safeName);
        }

        var parent = Path.GetDirectoryName(full);
        return parent is not null && Directory.Exists(parent)
            ? full
            : throw CliException.Usage($"資料夾不存在：{parent}");
    }

    private static async Task<string> WriteVerifiedAsync(CommandCall call, DownloadStream download, string target, bool force, string? expectedHash)
    {
        var temp = $"{target}.{Guid.NewGuid():N}.part";
        try
        {
            var (hash, written) = await CopyAsync(call.Api.Server, download, temp, call.CancellationToken);
            if (download.Length is { } length && length != written)
            {
                throw new CliException(ExitCodes.Error, "下載的大小和伺服器說的不一樣，沒有存檔。");
            }

            if (expectedHash is not null && !string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new CliException(ExitCodes.Error, "下載的內容和附件記的 sha256 不一樣，沒有存檔。請再試一次。");
            }

            MoveIntoPlace(temp, target, force);
            return hash;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>「已經存在」只可能是 rename 的當下才出現的檔案（前面檢查過了）；其他寫檔的錯誤是寫不進。</summary>
    private static void MoveIntoPlace(string temp, string target, bool force)
    {
        try
        {
            File.Move(temp, target, overwrite: force);
        }
        catch (IOException) when (!force && File.Exists(target))
        {
            throw CliException.Usage($"{target} 已經存在，沒有覆蓋。要蓋掉請加 --force。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliException(ExitCodes.Error, $"寫不進 {target}（{exception.GetType().Name}）。");
        }
    }

    /// <summary>讀網路與寫檔的例外分開：網路中斷照一般的連線錯誤說，不能說成寫不進檔案。</summary>
    private static async Task<(string Hash, long Written)> CopyAsync(string server, DownloadStream download, string temp, CancellationToken cancellationToken)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long written = 0;
        try
        {
            await using var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            var buffer = new byte[BufferSize];
            while (true)
            {
                var read = await ReadNetworkAsync(server, download.Content, buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                sha.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliException(ExitCodes.Error, $"寫不進 {temp}（{exception.GetType().Name}）。");
        }

        return (Convert.ToHexStringLower(sha.GetHashAndReset()), written);
    }

    private static async Task<int> ReadNetworkAsync(string server, Stream content, byte[] buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await content.ReadAsync(buffer, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw new CliException(ExitCodes.Error, $"連不上伺服器 {server}：{exception.Message}");
        }
    }
}
