using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

internal static class ProjectCommands
{
    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("project", "list", "列出看得到的專案（封存的會標出來）", "kumi project list [--json]", [], 0, 0, ListAsync),
        new("project", "rename", "改專案的名稱", "kumi project rename 代號 --name 名稱 [--json]",
            [new("name", "名稱", "新的專案名稱")], 1, 1, RenameAsync),
        new("project", "archive", "封存專案（封存後只能看，不能再改）", "kumi project archive 代號 [--json]", [], 1, 1, ArchiveAsync),
        new("project", "unarchive", "取消封存專案", "kumi project unarchive 代號 [--json]", [], 1, 1, UnarchiveAsync),
    ];

    private static async Task<int> ListAsync(CommandCall call)
    {
        var projects = await call.Api.GetAsync("projects", null, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, projects);
            return ExitCodes.Success;
        }

        var rows = projects.Items()
            .Select(project => (IReadOnlyList<string>)
                [project.Display("key"), NameWithArchiveMark(project), project.Display("blueprintVersion"), Labels.ProjectRole(project.Text("myRole"))])
            .ToArray();
        await TextTable.WriteAsync(call.Out, ["代號", "名稱", "藍圖版本", "我的角色"], rows);
        return ExitCodes.Success;
    }

    /// <summary>標在名稱後面而不是另開一欄：絕大多數專案沒封存，多一欄只會是一整排空白。</summary>
    internal static string NameWithArchiveMark(JsonElement project) =>
        project.Text("archivedAt") is null ? project.Display("name") : $"{project.Display("name")}（已封存）";

    private static async Task<int> RenameAsync(CommandCall call)
    {
        var key = call.Arguments.Positionals[0];
        var name = call.Arguments.Required("name").Trim();
        if (name.Length == 0)
        {
            throw CliException.Usage("--name 不能是空白。");
        }

        var project = await call.Api.PutAsync($"projects/{Uri.EscapeDataString(key)}/name", new JsonObject { ["name"] = name }, call.CancellationToken);
        return await ReportAsync(call, project, $"已把 {key} 改名為「{project.Text("name") ?? name}」。");
    }

    private static Task<int> ArchiveAsync(CommandCall call) => PostAsync(call, "archive", "已封存");

    private static Task<int> UnarchiveAsync(CommandCall call) => PostAsync(call, "unarchive", "已取消封存");

    private static async Task<int> PostAsync(CommandCall call, string action, string done)
    {
        var key = call.Arguments.Positionals[0];
        var project = await call.Api.PostAsync($"projects/{Uri.EscapeDataString(key)}/{action}", null, call.CancellationToken);
        return await ReportAsync(call, project, $"{done} {key}。");
    }

    private static async Task<int> ReportAsync(CommandCall call, JsonElement project, string message)
    {
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, project);
        }
        else
        {
            await call.Out.WriteLineAsync(message);
        }

        return ExitCodes.Success;
    }
}
