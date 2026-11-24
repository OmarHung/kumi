using System.Globalization;
using System.Text.Json.Nodes;
using PmTool.Cli.Output;

namespace PmTool.Cli.Agent;

internal sealed record JobSummary(AgentJob Job, JobOutcome Outcome, TimeSpan Elapsed, DateTimeOffset FinishedAt);

/// <summary>
/// 終端機的輸出：每件工作一行（時間、專案、引擎、模型、結果、秒數），<b>不印對話的內容</b>；<c>--json</c> 時每件一行 JSON。
/// 提示與警告一律寫標準錯誤，不混進給腳本讀的標準輸出。多件工作同時結束時用鎖，免得兩行混在一起。
/// </summary>
internal sealed class AgentConsole(TextWriter output, TextWriter error, bool json)
{
    private readonly object _gate = new();

    /// <param name="inBackground">開機自動啟動的背景程序（kumi agent start --service）：沒有視窗可以按 Ctrl-C，記錄裡改寫怎麼停。</param>
    public void Ready(string server, string machineName, IReadOnlyList<IAgentEngine> engines, bool inBackground = false)
    {
        if (json)
        {
            WriteJson(new JsonObject
            {
                ["event"] = "ready",
                ["server"] = server,
                ["machine"] = machineName,
                ["engines"] = new JsonArray([.. engines.Select(engine => (JsonNode?)new JsonObject
                {
                    ["kind"] = engine.Kind,
                    ["version"] = engine.Version,
                    ["models"] = engine.Models.Count,
                })]),
            });
            return;
        }

        var names = string.Join("、", engines.Select(engine => $"{EngineKinds.DisplayName(engine.Kind)} {engine.Version}{(engine.Detail is { } detail ? $"（{detail}）" : string.Empty)}"));
        var stop = inBackground ? "要停止請執行 kumi agent disable" : "按 Ctrl-C 結束";
        WriteLine(output, $"已連線 {server}，這台電腦叫「{machineName}」，提供 {names}。等網頁送來的回覆，{stop}。");
    }

    public void Notice(string message) => WriteLine(error, message);

    public void JobFinished(JobSummary summary)
    {
        var (job, outcome, elapsed, at) = (summary.Job, summary.Outcome, summary.Elapsed, summary.FinishedAt);
        var seconds = Math.Round(elapsed.TotalSeconds, 1);
        if (json)
        {
            WriteJson(new JsonObject
            {
                ["event"] = "job",
                ["time"] = at.ToString("O", CultureInfo.InvariantCulture),
                ["jobId"] = job.JobId,
                ["project"] = job.ProjectKey,
                ["engine"] = job.Engine,
                ["model"] = job.Model,
                ["status"] = outcome.Status,
                ["seconds"] = seconds,
                ["error"] = outcome.Error,
            });
            return;
        }

        var result = outcome.Status switch
        {
            JobStatus.Complete => "完成",
            JobStatus.Stopped => "中斷",
            _ => "失敗",
        };
        var reason = outcome.Error is { Length: > 0 } text ? $"：{text}" : string.Empty;
        WriteLine(output,
            $"{at.ToLocalTime():HH:mm:ss}  {job.ProjectKey}  {EngineKinds.DisplayName(job.Engine)}  {job.Model}  {result}{reason}  {seconds.ToString("0.0", CultureInfo.InvariantCulture)} 秒");
    }

    private void WriteJson(JsonObject line)
    {
        // 一行一個 JSON：不縮排。
        WriteLine(output, line.ToJsonString());
    }

    private void WriteLine(TextWriter writer, string text)
    {
        lock (_gate)
        {
            writer.WriteLine(text);
            writer.Flush();
        }
    }
}
