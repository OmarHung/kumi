using PmTool.Abstractions;
using PmTool.Ai.LocalAgents;

namespace PmTool.Ai.HostAgents;

/// <summary>檢查過的登記內容。</summary>
internal sealed record ValidatedCheckIn(string AgentVersion, string ServerUrl, IReadOnlyList<StoredHostEngine> Engines);

/// <summary>
/// worker 送來的內容的檢查（internal/HANDOFF-host-agent.md 第 6.4 節）。worker 是自己的程式，但內容會存進資料庫、顯示在擁有者的畫面上，
/// 一樣當外部資料處理。模型的規則和本機代理同一套（<see cref="LocalAgentValidator"/>），失敗代碼換成 <see cref="AiErrorCodes.HostAgentInvalid"/>。
/// </summary>
internal static class HostValidator
{
    public static Result<(string MachineKey, string MachineName, string Version)> Pairing(HostPairingRequest request)
    {
        if (!LocalAgentValidator.IsMachineKey(request.MachineKey))
        {
            return HostFailures.AgentInvalid(
                "machineKey", $"機器編號要是 {HostLimits.MinMachineKeyLength}～{HostLimits.MaxMachineKeyLength} 個英數字與「-」。");
        }

        if (LocalAgentValidator.CleanLine(request.MachineName, AiRules.MaxMachineNameLength) is not { } name)
        {
            return HostFailures.AgentInvalid("machineName", $"主機名稱要是 1～{AiRules.MaxMachineNameLength} 個字，不能有控制字元。");
        }

        return LocalAgentValidator.CleanLine(request.AgentVersion, HostLimits.MaxVersionLength) is { } version
            ? (request.MachineKey, name, version)
            : HostFailures.AgentInvalid("agentVersion", $"版本要是 1～{HostLimits.MaxVersionLength} 個字，不能有控制字元。");
    }

    public static Result<ValidatedCheckIn> CheckIn(HostAgentCheckIn input)
    {
        if (LocalAgentValidator.CleanLine(input.AgentVersion, HostLimits.MaxVersionLength) is not { } version)
        {
            return HostFailures.AgentInvalid("agentVersion", $"版本要是 1～{HostLimits.MaxVersionLength} 個字，不能有控制字元。");
        }

        if (CleanServerUrl(input.ServerUrl) is not { } serverUrl)
        {
            return HostFailures.AgentInvalid("serverUrl", "伺服器網址要是 http 或 https 的完整網址，不能帶帳號密碼、? 或 #。");
        }

        var engines = new List<StoredHostEngine>();
        foreach (var report in input.Engines ?? [])
        {
            var checkedEngine = CheckEngine(report, engines.Count);
            if (checkedEngine.Failure is { } failure)
            {
                return failure;
            }

            if (engines.Any(e => e.Kind == checkedEngine.Value.Kind))
            {
                return HostFailures.AgentInvalid("engines", "同一種引擎不能重複。");
            }

            engines.Add(checkedEngine.Value);
        }

        return new ValidatedCheckIn(version, serverUrl, engines);
    }

    private static string? CleanServerUrl(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text)
            || text.Length > AiRules.MaxBaseUrlLength
            || text.Any(char.IsControl)
            || text.Contains('?', StringComparison.Ordinal)
            || text.Contains('#', StringComparison.Ordinal)
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return text.TrimEnd('/');
    }

    private static Result<StoredHostEngine> CheckEngine(HostEngineReport? report, int index)
    {
        var path = $"engines[{index}]";
        if (report is null || !HostEnums.IsEngine(report.Kind))
        {
            return HostFailures.AgentInvalid($"{path}.kind", "引擎只能是 Claude Code 或 Codex。");
        }

        if (!Enum.IsDefined(report.Billing))
        {
            return HostFailures.AgentInvalid($"{path}.billing", "計費方式只能是 api、subscription、unknown。");
        }

        string? version = null;
        if (report.Version is not null && (version = LocalAgentValidator.CleanLine(report.Version, HostLimits.MaxVersionLength)) is null)
        {
            return HostFailures.AgentInvalid($"{path}.version", $"引擎的版本最長 {HostLimits.MaxVersionLength} 個字，不能有控制字元。");
        }

        var models = CheckModels(report, path);
        if (models.Failure is { } failure)
        {
            return failure;
        }

        var problem = report.Problem is null ? null : AiText.Cut(LocalAgentValidator.CleanText(report.Problem), AiRules.MaxErrorLength);
        return new StoredHostEngine(
            report.Kind,
            report.Installed,
            version,
            report.Installed && report.Supported,
            report.Installed && report.LoggedIn,
            report.Billing,
            report.LoginId,
            report.Installed && report.Supported ? models.Value : [],
            problem);
    }

    private static Result<IReadOnlyList<AiModelOption>> CheckModels(HostEngineReport report, string path)
    {
        var models = report.Models ?? [];
        if (models.Count > AiRules.MaxLocalEngineModels)
        {
            return HostFailures.AgentInvalid($"{path}.models", $"一個引擎最多列 {AiRules.MaxLocalEngineModels} 個模型。");
        }

        var cleaned = new List<AiModelOption>();
        foreach (var model in models)
        {
            var checkedModel = LocalAgentValidator.CheckModel(model, $"{path}.models[{cleaned.Count}]");
            if (checkedModel.Failure is { } failure)
            {
                return HostFailures.AgentInvalid(failure.Details.FirstOrDefault()?.Path ?? $"{path}.models", failure.Message);
            }

            if (cleaned.Any(m => m.Id == checkedModel.Value.Id))
            {
                return HostFailures.AgentInvalid($"{path}.models", "模型的代號不能重複。");
            }

            cleaned.Add(checkedModel.Value);
        }

        return cleaned;
    }
}
