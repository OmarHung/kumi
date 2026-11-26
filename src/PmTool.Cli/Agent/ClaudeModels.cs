using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PmTool.Cli.Api;

namespace PmTool.Cli.Agent;

/// <summary>
/// Claude Code 的別名（opus、sonnet、haiku、預設）實際指到哪個模型：啟動一次無頭的引擎，讀到 <c>system/init</c> 的 <c>model</c> 就結束。
/// init 在送出任何模型請求之前就印出來，所以不會用到訂閱額度。版本只放在顯示名稱，模型的 id 仍是別名，
/// Claude Code 更新之後別名自然指到新的模型。
/// </summary>
internal static partial class ClaudeModels
{
    /// <summary>
    /// 把 <c>claude-opus-5-5</c> 轉成 <c>Opus 5.5</c>；日期的尾巴（<c>-20250805</c>）與 <c>[1m]</c> 略過；
    /// 看不懂的照原代號。
    /// </summary>
    public static string DisplayName(string modelId)
    {
        var match = ModelPattern().Match(modelId.Trim());
        if (!match.Success)
        {
            return modelId;
        }

        var family = match.Groups["family"].Value;
        var version = string.Join('.', match.Groups["version"].Value.Split('-'));
        return string.Create(CultureInfo.InvariantCulture, $"{char.ToUpperInvariant(family[0])}{family[1..]} {version}");
    }

    /// <summary>
    /// 模型沒指定思考深度時 Claude Code 用的預設，出處是官方文件 code.claude.com/docs/en/model-config（Effort level 一節）：
    /// Fable 5.2、Opus 5.5、Sonnet 5.5、Haiku 5.5 是 medium，Opus 4.7 是 xhigh，其他是 high。比對前拿掉日期與 [1m]。
    /// </summary>
    public static string? DefaultEffortOf(string modelId)
    {
        var match = ModelPattern().Match(modelId.Trim());
        var normalized = match.Success ? $"claude-{match.Groups["family"].Value}-{match.Groups["version"].Value}" : modelId;
        if (!normalized.StartsWith("claude-", StringComparison.Ordinal))
        {
            return null;
        }

        return normalized switch
        {
            "claude-fable-5-2" or "claude-opus-5-5" or "claude-sonnet-5-5" or "claude-haiku-5-5" => "medium",
            "claude-opus-4-7" => "xhigh",
            _ => "high",
        };
    }

    /// <summary>四個選項各探一次（平行）；失敗或逾時的不在結果裡，呼叫端退回原本的名稱。key 是別名，default 是不帶 --model。</summary>
    public static async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        string executable, AgentOptions options, CancellationToken cancellation)
    {
        var runner = new ProcessRunner(options);
        var aliases = ClaudeCodeSpec.Models.Select(model => model.Id).ToArray();
        var resolved = await Task.WhenAll(aliases.Select(alias => ProbeAsync(runner, executable, alias, options, cancellation)));
        return aliases.Zip(resolved).Where(pair => pair.Second is not null).ToDictionary(pair => pair.First, pair => pair.Second!);
    }

    /// <summary>列在模型清單裡的顯示名稱：預設寫成「預設（Opus 5.5）」，其他寫 Opus 5.5；探不到就用原本的名稱。</summary>
    public static IReadOnlyList<EngineModel> WithResolvedNames(IReadOnlyDictionary<string, string> resolved) =>
    [
        .. ClaudeCodeSpec.Models.Select(model => !resolved.TryGetValue(model.Id, out var id)
            ? model
            : model with
            {
                DisplayName = model.Id == EngineModel.DefaultId ? $"預設（{DisplayName(id)}）" : DisplayName(id),
                DefaultEffort = DefaultEffortOf(id) is { } effort && model.Efforts.Contains(effort) ? effort : null,
            }),
    ];

    private static async Task<string?> ProbeAsync(
        ProcessRunner runner, string executable, string alias, AgentOptions options, CancellationToken cancellation)
    {
        var directory = Directory.CreateTempSubdirectory("kumi-model-");
        try
        {
            var config = Path.Combine(directory.FullName, "mcp.json");
            await File.WriteAllTextAsync(config, """{"mcpServers":{}}""", cancellation);
            var arguments = new List<string>
            {
                "-p", "--output-format", "stream-json", "--verbose",
                "--strict-mcp-config", "--mcp-config", config, "--tools", string.Empty,
                "--setting-sources", "project", "--disable-slash-commands", "--no-session-persistence", "--permission-mode", "dontAsk",
            };
            if (alias != EngineModel.DefaultId)
            {
                arguments.AddRange(["--model", alias]);
            }

            string? model = null;
            await runner.RunAsync(
                new ProcessRequest(executable, arguments, directory.FullName, "hi", new Dictionary<string, string>()),
                line =>
                {
                    model = ModelFromInit(line);
                    // 讀到 init 就整個結束：再等下去就會送出模型請求。
                    return model is null ? LineVerdict.Continue : LineVerdict.Abort;
                },
                options.ModelProbeTimeout,
                cancellation);
            return model;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 刪不掉就算了。
            }
        }
    }

    private static string? ModelFromInit(string line)
    {
        using var document = TranslatorSupport.TryParse(line);
        if (document is null || document.RootElement.Text("type") != "system" || document.RootElement.Text("subtype") != "init")
        {
            return null;
        }

        return document.RootElement.Text("model") is { Length: > 0 and <= 100 } model && !model.Any(char.IsControl) ? model : null;
    }

    [GeneratedRegex(@"^claude-(?<family>[a-z]+)-(?<version>\d+(?:-\d{1,2})*)(?:-\d{8})?(?:\[[^\]]*\])?\z")]
    private static partial Regex ModelPattern();
}
