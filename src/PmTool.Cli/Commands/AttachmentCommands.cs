using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>項目的附件：上傳、列出、下載。能不能做、檔名合不合規則由伺服器判斷，這裡只先擋明顯不行的。</summary>
internal static class AttachmentCommands
{
    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("item", "attach", "把檔案附加到項目（一個檔案送一次，單檔最大 25 MiB）", "kumi item attach 編號 檔案...",
            [], 2, int.MaxValue, AttachAsync),
        new("item", "attachments", "列出項目的附件", "kumi item attachments 編號 [--json]", [], 1, 1, ListAsync),
        new("attachment", "download", "下載附件", "kumi attachment download 編號 [-o 路徑] [--item 項目編號] [--force]",
            [new("output", "路徑", "存到這個檔案；是資料夾的話放在裡面，用原本的檔名", 'o'),
             new("item", "項目編號", "附件所在的項目；給了就用列出的檔名與 sha256（預設用下載回應的標頭）"),
             new("force", "", "目標已經存在時覆蓋它")],
            1, 1, AttachmentDownload.RunAsync),
    ];

    private static async Task<int> AttachAsync(CommandCall call)
    {
        var itemKey = call.Arguments.Positionals[0];
        var path = $"items/{Uri.EscapeDataString(itemKey)}/attachments";
        var uploaded = new List<JsonElement>();
        var exitCode = ExitCodes.Success;
        foreach (var file in call.Arguments.Positionals.Skip(1))
        {
            // 一個失敗不影響其他的；結束代碼用第一個失敗的。
            var failure = await TryUploadAsync(call, path, file, uploaded);
            if (failure is { } code && exitCode == ExitCodes.Success)
            {
                exitCode = code;
            }
        }

        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, new JsonArray([.. uploaded.Select(item => JsonNode.Parse(item.GetRawText()))]));
        }

        return exitCode;
    }

    /// <summary>送一個檔案。成功回 null；失敗把原因寫到標準錯誤，回結束代碼。</summary>
    private static async Task<int?> TryUploadAsync(CommandCall call, string path, string file, List<JsonElement> uploaded)
    {
        var fullPath = Path.GetFullPath(file, call.Context.WorkingDirectory);
        if (AttachmentFiles.Problem(fullPath) is { } problem)
        {
            await call.Error.WriteLineAsync($"{file}：{problem}");
            return ExitCodes.Invalid;
        }

        var name = Path.GetFileName(fullPath);
        try
        {
            var api = call.Api;
            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            var attachment = await api.PostFileAsync(path, stream, name, AttachmentFiles.ContentTypeOf(name), call.CancellationToken);
            uploaded.Add(attachment);
            if (!call.Json)
            {
                var size = attachment.Number("sizeBytes") ?? new FileInfo(fullPath).Length;
                await call.Out.WriteLineAsync($"已附加 {attachment.Text("fileName") ?? name}（{FileSizes.Format(size)}）");
            }

            return null;
        }
        catch (CliException exception)
        {
            await call.Error.WriteLineAsync($"{file}：{exception.Message}");
            foreach (var (_, message) in exception.Details.Where(detail => detail.Message != exception.Message))
            {
                await call.Error.WriteLineAsync($"  {message}");
            }

            return exception.ExitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await call.Error.WriteLineAsync($"{file}：讀檔時失敗。");
            return ExitCodes.Error;
        }
    }

    private static async Task<int> ListAsync(CommandCall call)
    {
        var key = call.Arguments.Positionals[0];
        var data = await call.Api.GetAsync($"items/{Uri.EscapeDataString(key)}/attachments", null, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, data);
            return ExitCodes.Success;
        }

        var attachments = data.Items();
        if (attachments.Count == 0)
        {
            await call.Out.WriteLineAsync($"{key} 沒有附件。");
            return ExitCodes.Success;
        }

        var rows = attachments
            .Select(item => (IReadOnlyList<string>)
            [
                item.Display("id"), item.Display("fileName"),
                item.Number("sizeBytes") is { } size ? FileSizes.Format(size) : "-",
                item.Get("uploadedBy")?.Display("displayName") ?? "-", JsonAccess.Moment(item.Text("uploadedAt")),
            ])
            .ToArray();
        await TextTable.WriteAsync(call.Out, ["編號", "檔名", "大小", "上傳的人", "時間"], rows);
        return ExitCodes.Success;
    }
}
