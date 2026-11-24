using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint;
using PmTool.Cli.Api;
using PmTool.Cli.Documents;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>
/// 藍圖的四個入口之一：匯出成檔案、本機驗證、預覽差異、套用。
/// 匯出的檔案就是變更集的請求，改完直接 plan、apply。
/// </summary>
internal static class BlueprintCommands
{
    private const string BlueprintReadPermission = "blueprint.read";

    private static readonly OptionSpec Project = new("project", "代號", "專案代號（YAML 檔第一行的 # project: 也算）", 'p');
    private static readonly OptionSpec FileOption = new("file", "檔案", "變更集文件（JSON 或 YAML），- 是標準輸入", 'f');

    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("blueprint", "export", "把專案目前的藍圖匯出成可以改了再套用的檔案",
            "kumi blueprint export --project 代號 [-o 檔案] [--format yaml|json]",
            [new("project", "代號", "專案代號", 'p'), new("output", "檔案", "寫到檔案；沒給就印到標準輸出", 'o'),
             new("format", "格式", "yaml（預設）或 json")], 0, 0, ExportAsync),
        new("blueprint", "validate", "只在本機檢查文件，不連伺服器", "kumi blueprint validate -f 檔案 [--json]", [FileOption], 0, 0, ValidateAsync),
        new("blueprint", "plan", "預覽套用之後的差異與對資料的影響，什麼都不存", "kumi blueprint plan -f 檔案 [--project 代號]",
            [FileOption, Project], 0, 0, PlanAsync),
        new("blueprint", "apply", "預覽、送出，有權限就直接套用", "kumi blueprint apply -f 檔案 [--project 代號] [--yes] [--summary 說明]",
            [FileOption, Project, new("yes", "", "會動到既有資料時不再詢問", 'y'), new("summary", "說明", "給核准的人看的一句話")], 0, 0, ApplyAsync),
    ];

    private static async Task<int> ExportAsync(CommandCall call)
    {
        var projectKey = call.Arguments.Required("project");
        var format = ExportFormat(call);
        var detail = await call.Api.GetAsync($"projects/{Uri.EscapeDataString(projectKey)}", null, call.CancellationToken);
        if (!detail.List("myPermissions").Any(permission => permission.GetString() == BlueprintReadPermission))
        {
            throw new CliException(
                ExitCodes.Error, "你在這個專案沒有查看專案設定的權限，匯出的藍圖會不完整，所以不匯出。");
        }

        var project = detail.Get("project") ?? default;
        var text = BlueprintExport.Render(
            project.Text("key") ?? projectKey, project.Number("blueprintVersion") ?? 0, detail.Get("blueprint") ?? default, format);
        if (call.Arguments.Value("output") is not { } path)
        {
            await call.Out.WriteAsync(text);
            return ExitCodes.Success;
        }

        await WriteFileAsync(path, text, call.CancellationToken);
        if (call.Json)
        {
            return ExitCodes.Success;
        }

        await call.Out.WriteLineAsync($"已匯出專案 {project.Display("key")} 的藍圖（第 {project.Display("blueprintVersion")} 版）到 {path}。");
        return ExitCodes.Success;
    }

    private static async Task<int> ValidateAsync(CommandCall call)
    {
        var document = await DocumentText.ReadAsync(call.Arguments.Required("file"), call.Context.In, call.CancellationToken);
        var keys = document.TopLevelKeys();
        var isBlueprint = keys is not null && keys.Contains("apiVersion") && !keys.Contains("baseVersion");
        var validator = BlueprintValidator.CreateDefault();
        var report = isBlueprint
            ? validator.ValidateBlueprint(document.Text, document.Format).Report
            : validator.ValidateChangeSet(document.Text, document.Format).Report;
        var kind = isBlueprint ? "blueprint" : "changeset";

        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, new JsonObject
            {
                ["valid"] = report.IsValid,
                ["kind"] = kind,
                ["issues"] = new JsonArray([.. report.Issues.Select(issue => (JsonNode?)new JsonObject
                {
                    ["code"] = issue.Code, ["path"] = issue.Path, ["message"] = issue.Message,
                })]),
            });
            return report.IsValid ? ExitCodes.Success : ExitCodes.Invalid;
        }

        if (report.IsValid)
        {
            await call.Out.WriteLineAsync($"{document.Source} 沒有問題（這是一份{(isBlueprint ? "藍圖文件" : "變更集")}）。");
            return ExitCodes.Success;
        }

        await call.Error.WriteLineAsync($"{document.Source} 有 {report.Issues.Count}{(report.Truncated ? " 個以上" : " 個")}問題：");
        foreach (var issue in report.Issues)
        {
            await call.Error.WriteLineAsync($"  {(issue.Path.Length == 0 ? "（整份文件）" : issue.Path)}：{issue.Message}（{issue.Code}）");
        }

        return ExitCodes.Invalid;
    }

    private static async Task<int> PlanAsync(CommandCall call)
    {
        var (projectKey, body, format) = await LoadAsync(call);
        var preview = await call.Api.PostDocumentAsync(PreviewPath(projectKey), body, format, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, preview);
        }
        else
        {
            await WritePreviewAsync(call, projectKey, preview);
        }

        return PlanPrinter.NeedsDecision(preview) ? ExitCodes.NeedsDecision : ExitCodes.Success;
    }

    private static async Task<int> ApplyAsync(CommandCall call)
    {
        var (projectKey, body, format) = await LoadAsync(call);
        var preview = await call.Api.PostDocumentAsync(PreviewPath(projectKey), body, format, call.CancellationToken);
        if (!call.Json)
        {
            await WritePreviewAsync(call, projectKey, preview);
        }

        if (preview.List("changes").Count == 0 && preview.List("blockers").Count == 0)
        {
            await StopAsync(call, preview, "不需要套用。");
            return ExitCodes.Success;
        }

        if (preview.List("blockers").Count > 0)
        {
            await StopAsync(call, preview, "沒有送出。");
            return ExitCodes.NeedsDecision;
        }

        if (PlanPrinter.TotalAffected(preview) > 0 && !call.Arguments.Has("yes") && !await ConfirmAsync(call))
        {
            await StopAsync(call, preview, "沒有送出。要略過確認，加 --yes。");
            return ExitCodes.NeedsDecision;
        }

        return await SubmitAsync(call, projectKey, body, format, canApply: preview.Flag("canApply"));
    }

    private static async Task<int> SubmitAsync(CommandCall call, string projectKey, string body, DocumentFormat format, bool canApply)
    {
        var path = $"projects/{Uri.EscapeDataString(projectKey)}/changesets";
        var proposed = await call.Api.PostDocumentAsync(path, body, format, call.CancellationToken);
        var number = proposed.Get("changeSet")?.Number("number") is { } found and > 0
            ? found
            : throw new CliException(
                ExitCodes.Error,
                $"文件已送出，但伺服器沒有回變更集的編號，所以沒有套用。請用 kumi changeset list --project {projectKey} 找到它，再用 kumi changeset apply 編號 --project {projectKey} 套用。");
        var result = canApply ? await ApplySubmittedAsync(call, path, number, projectKey) : proposed;

        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, result);
        }
        else if (canApply)
        {
            await call.Out.WriteLineAsync($"已套用 #{number}，專案的藍圖現在是第 {result.Get("changeSet")?.Display("appliedVersion") ?? "-"} 版。");
        }
        else
        {
            await call.Out.WriteLineAsync($"已送出 #{number}，等待有核准權限的人套用。");
        }

        return ExitCodes.Success;
    }

    /// <summary>送出成功之後套用失敗（資料在這之間變了、逾時、斷線）：變更集已經在伺服器上，要讓使用者知道編號與接下來能做什麼。</summary>
    private static async Task<JsonElement> ApplySubmittedAsync(CommandCall call, string path, long number, string projectKey)
    {
        try
        {
            return await call.Api.PostAsync($"{path}/{number}/apply", null, call.CancellationToken);
        }
        catch (CliException failure)
        {
            throw new CliException(
                failure.ExitCode,
                $"已送出 #{number}，還沒套用：{failure.Message} "
                + $"可以用 kumi changeset apply {number} --project {projectKey} 再試一次，或用 kumi changeset reject {number} --project {projectKey} 拒絕它。",
                failure.Details);
        }
    }

    private static async Task StopAsync(CommandCall call, JsonElement preview, string message)
    {
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, preview);
        }
        else
        {
            await call.Out.WriteLineAsync(message);
        }
    }

    private static async Task<bool> ConfirmAsync(CommandCall call)
    {
        // --json 是給腳本用的：就算接在終端機上也不問，沒有 --yes 就當作不同意。
        if (!call.Context.IsInteractive || call.Json)
        {
            return false;
        }

        await call.Error.WriteAsync("這會動到既有的資料。要套用嗎？[y/N] ");
        var answer = (await call.Context.In.ReadLineAsync(call.CancellationToken))?.Trim();
        return answer is not null && (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WritePreviewAsync(CommandCall call, string projectKey, JsonElement preview)
    {
        await call.Out.WriteLineAsync(
            $"專案 {projectKey}：這份文件以第 {preview.Display("baseVersion")} 版為基準，專案目前是第 {preview.Display("currentVersion")} 版。");
        await call.Out.WriteLineAsync();
        await PlanPrinter.WriteChangesAndImpactsAsync(call.Out, preview);
        await PlanPrinter.WriteBlockersAsync(call.Out, preview);
    }

    /// <summary>讀檔、決定專案；有 --summary 時把它放進文件，沒有就照原文送，伺服器報的行號才對得上檔案。</summary>
    private static async Task<(string ProjectKey, string Body, DocumentFormat Format)> LoadAsync(CommandCall call)
    {
        var document = await DocumentText.ReadAsync(call.Arguments.Required("file"), call.Context.In, call.CancellationToken);
        var projectKey = ResolveProject(call.Arguments.Value("project"), document);
        if (call.Arguments.Value("summary") is not { } summary)
        {
            return (projectKey, document.Text, document.Format);
        }

        // 用和伺服器一樣的讀法讀進來再加上 summary：自己另寫一套讀法，0x10、很大的整數、有引號的數字都會悄悄變掉。
        var parsed = document.Parse();
        if (!parsed.Succeeded || parsed.Root is not JsonObject root)
        {
            throw new CliException(
                ExitCodes.Invalid,
                $"{document.Source} 讀不懂或不是一份文件物件，沒辦法加上 --summary。先用 kumi blueprint validate 檢查。",
                [.. parsed.Issues.Select(issue => (Path: string.Empty, issue.Message))]);
        }

        root["summary"] = summary;
        return (projectKey, DocumentText.ToJsonText(root), DocumentFormat.Json);
    }

    private static string ResolveProject(string? option, DocumentText document)
    {
        var fromFile = document.ProjectKeyFromComment();
        if (option is not null && fromFile is not null && !string.Equals(option, fromFile, StringComparison.OrdinalIgnoreCase))
        {
            throw CliException.Usage($"{document.Source}第一行寫的專案是 {fromFile}，但 --project 給的是 {option}。");
        }

        return option ?? fromFile ?? throw CliException.Usage(
            $"不知道要對哪個專案。請加 --project 代號；YAML 檔第一行寫 `# project: 代號` 也可以（JSON 檔沒有地方放，要用 --project）。");
    }

    private static string PreviewPath(string projectKey) => $"projects/{Uri.EscapeDataString(projectKey)}/changesets/preview";

    private static DocumentFormat ExportFormat(CommandCall call)
    {
        var name = call.Arguments.Value("format") ?? (call.Json ? "json" : null);
        if (name is null && call.Arguments.Value("output") is { } path && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentFormat.Json;
        }

        return name?.ToLowerInvariant() switch
        {
            null or "yaml" or "yml" => DocumentFormat.Yaml,
            "json" => DocumentFormat.Json,
            _ => throw CliException.Usage($"--format 只能是 yaml 或 json，收到：{name}。"),
        };
    }

    private static async Task WriteFileAsync(string path, string text, CancellationToken cancellationToken)
    {
        try
        {
            await System.IO.File.WriteAllTextAsync(path, text, new System.Text.UTF8Encoding(false), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliException(ExitCodes.Error, $"寫不進 {path}：{exception.Message}");
        }
    }
}
