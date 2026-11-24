using System.Text;
using PmTool.Abstractions;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 本機代理送來的內容的檢查（internal/HANDOFF-local-agent.md 第 4.2 節）。送來的是使用者自己電腦上的程式，
/// 但它的內容會存進資料庫、顯示在別的畫面上，一樣當外部資料處理：NUL 與控制字元一律擋或換掉。
/// </summary>
internal static class LocalAgentValidator
{
    private const string MachineKeyPath = "machineKey";
    private const string MachineNamePath = "machineName";
    private const string AgentVersionPath = "agentVersion";
    private const string EnginesPath = "engines";

    public static Result<LocalAgentCheckIn> CheckIn(LocalAgentCheckIn input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!IsMachineKey(input.MachineKey))
        {
            return LocalAgentFailures.Invalid(
                MachineKeyPath, $"電腦編號要是 {LocalAgentLimits.MinMachineKeyLength}～{LocalAgentLimits.MaxMachineKeyLength} 個英數字與「-」。");
        }

        if (CleanLine(input.MachineName, AiRules.MaxMachineNameLength) is not { } name)
        {
            return LocalAgentFailures.Invalid(MachineNamePath, $"電腦名稱要是 1～{AiRules.MaxMachineNameLength} 個字，不能有控制字元。");
        }

        if (CleanLine(input.AgentVersion, LocalAgentLimits.MaxVersionLength) is not { } version)
        {
            return LocalAgentFailures.Invalid(AgentVersionPath, $"代理的版本要是 1～{LocalAgentLimits.MaxVersionLength} 個字，不能有控制字元。");
        }

        var engines = new List<LocalEngineInfo>();
        foreach (var engine in input.Engines ?? [])
        {
            var checkedEngine = CheckEngine(engine, engines.Count);
            if (checkedEngine.Failure is { } failure)
            {
                return failure;
            }

            if (engines.Any(e => e.Kind == checkedEngine.Value.Kind))
            {
                return LocalAgentFailures.Invalid(EnginesPath, "同一種引擎不能重複。");
            }

            engines.Add(checkedEngine.Value);
        }

        return new LocalAgentCheckIn(input.MachineKey, name, version, engines);
    }

    private static Result<LocalEngineInfo> CheckEngine(LocalEngineInfo engine, int index)
    {
        var path = $"{EnginesPath}[{index}]";
        if (engine is null || engine.Kind is not (AiProviderKind.ClaudeCode or AiProviderKind.Codex))
        {
            return LocalAgentFailures.Invalid($"{path}.kind", "引擎只能是 Claude Code 或 Codex。");
        }

        if (CleanLine(engine.Version, LocalAgentLimits.MaxVersionLength) is not { } version)
        {
            return LocalAgentFailures.Invalid($"{path}.version", $"引擎的版本要是 1～{LocalAgentLimits.MaxVersionLength} 個字，不能有控制字元。");
        }

        var models = engine.Models ?? [];
        if (models.Count > AiRules.MaxLocalEngineModels)
        {
            return LocalAgentFailures.Invalid($"{path}.models", $"一個引擎最多列 {AiRules.MaxLocalEngineModels} 個模型。");
        }

        var cleaned = new List<AiModelOption>();
        foreach (var model in models)
        {
            var checkedModel = CheckModel(model, $"{path}.models[{cleaned.Count}]");
            if (checkedModel.Failure is { } failure)
            {
                return failure;
            }

            if (cleaned.Any(m => m.Id == checkedModel.Value.Id))
            {
                return LocalAgentFailures.Invalid($"{path}.models", "模型的代號不能重複。");
            }

            cleaned.Add(checkedModel.Value);
        }

        return new LocalEngineInfo(engine.Kind, version, cleaned);
    }

    internal static Result<AiModelOption> CheckModel(AiModelOption model, string path)
    {
        if (model is null
            || string.IsNullOrEmpty(model.Id)
            || model.Id.Length > AiRules.MaxModelIdLength
            || model.Id.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            return LocalAgentFailures.Invalid($"{path}.id", $"模型代號要是 1～{AiRules.MaxModelIdLength} 個字，不能有空白與控制字元。");
        }

        if (CleanLine(model.DisplayName, AiRules.MaxDisplayNameLength) is not { } displayName)
        {
            return LocalAgentFailures.Invalid($"{path}.displayName", $"模型名稱要是 1～{AiRules.MaxDisplayNameLength} 個字，不能有控制字元。");
        }

        var efforts = model.Efforts ?? [];
        if (efforts.Any(e => !Enum.IsDefined(e)) || efforts.Distinct().Count() != efforts.Count)
        {
            return LocalAgentFailures.Invalid($"{path}.efforts", "思考深度只能是 none、low、medium、high、xhigh、max，而且不能重複。");
        }

        // 引擎回報的預設不在能選的等級裡時當作不知道，不為了它整個擋掉登記。
        var defaultEffort = model.DefaultEffort is { } reported && efforts.Contains(reported) ? reported : (AiEffort?)null;
        return new AiModelOption(model.Id, displayName, efforts) { DefaultEffort = defaultEffort };
    }

    /// <summary>一則進度。文字去掉 NUL、其他控制字元（換行與 Tab 除外）換成空白；工具的欄位照規則檢查。</summary>
    public static Result<LocalAgentEvent> Progress(LocalAgentEvent progress, int index)
    {
        var path = $"events[{index}]";
        switch (progress)
        {
            case LocalAgentText text:
                if (text.Delta is null || text.Delta.Length > AiRules.MaxLocalAgentTextDelta)
                {
                    return LocalAgentFailures.Invalid($"{path}.delta", $"一則文字最長 {AiRules.MaxLocalAgentTextDelta} 個字。");
                }

                return new LocalAgentText(CleanText(text.Delta));
            case LocalAgentTool { Tool: { } tool }:
                return CheckTool(tool, path);
            default:
                return LocalAgentFailures.Invalid(path, "不認得的進度種類。");
        }
    }

    private static Result<LocalAgentEvent> CheckTool(AssistantToolPart tool, string path)
    {
        if (string.IsNullOrEmpty(tool.CallId) || tool.CallId.Length > LocalAgentLimits.MaxCallIdLength || tool.CallId.Any(char.IsControl))
        {
            return LocalAgentFailures.Invalid($"{path}.callId", $"工具呼叫的編號要是 1～{LocalAgentLimits.MaxCallIdLength} 個字，不能有控制字元。");
        }

        if (!IsToolName(tool.Name))
        {
            return LocalAgentFailures.Invalid($"{path}.name", $"工具名稱要是 1～{LocalAgentLimits.MaxToolNameLength} 個英數字與「_」。");
        }

        if (!Enum.IsDefined(tool.Status))
        {
            return LocalAgentFailures.Invalid($"{path}.status", "工具的狀態只能是 running、done、failed。");
        }

        if (tool.ChangeSetNumber is < 0)
        {
            return LocalAgentFailures.Invalid($"{path}.changeSetNumber", "變更集編號不能是負的。");
        }

        if (tool.Error is { Length: > AiRules.MaxErrorLength })
        {
            return LocalAgentFailures.Invalid($"{path}.error", $"錯誤說明最長 {AiRules.MaxErrorLength} 個字。");
        }

        return new LocalAgentTool(tool with { Error = tool.Error is null ? null : CleanText(tool.Error) });
    }

    /// <summary>NUL 拿掉（jsonb 不收）、其他控制字元（換行與 Tab 除外）換成空白。</summary>
    public static string CleanText(string text)
    {
        if (!text.Any(char.IsControl))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == '\0')
            {
                continue;
            }

            builder.Append(char.IsControl(c) && c is not ('\n' or '\r' or '\t') ? ' ' : c);
        }

        return builder.ToString();
    }

    internal static bool IsMachineKey(string? key) =>
        key is { Length: >= LocalAgentLimits.MinMachineKeyLength and <= LocalAgentLimits.MaxMachineKeyLength }
        && key.All(c => c is '-' or (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'));

    private static bool IsToolName(string? name) =>
        name is { Length: >= 1 and <= LocalAgentLimits.MaxToolNameLength }
        && name.All(c => c is '_' or (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'));

    /// <summary>修掉頭尾空白後 1～<paramref name="max"/> 個字、沒有任何控制字元的單行文字；不合規定是 null。</summary>
    internal static string? CleanLine(string? raw, int max)
    {
        var text = raw?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > max || text.Any(char.IsControl) ? null : text;
    }
}
