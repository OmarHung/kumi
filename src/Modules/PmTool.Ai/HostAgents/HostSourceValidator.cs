using PmTool.Abstractions;

namespace PmTool.Ai.HostAgents;

/// <summary>檢查過、整理好的組織來源欄位。</summary>
internal sealed record ValidatedSource(
    string Name,
    string NameKey,
    IReadOnlyList<string> EnabledModels,
    string? DefaultModel,
    AiEffort? DefaultEffort,
    HostSourceLimits Limits);

/// <summary>
/// 組織來源欄位的檢查（internal/HANDOFF-host-agent.md 第 6.3 節）。細項的路徑是 camelCase 的欄位名稱。
/// 模型要在引擎最後一次回報的清單裡；引擎還沒回報過清單時，關著的來源可以先存（開著的不行）。
/// </summary>
internal static class HostSourceValidator
{
    private const string NamePath = "name";
    private const string ModelsPath = "enabledModels";
    private const string DefaultModelPath = "defaultModel";
    private const string DefaultEffortPath = "defaultEffort";

    /// <summary>修掉頭尾空白後 1～<see cref="AiRules.MaxNameLength"/> 個字、沒有控制字元；不合規定是 null。</summary>
    public static string? CleanName(string? raw)
    {
        var text = raw?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > AiRules.MaxNameLength || text.Any(char.IsControl) ? null : text;
    }

    /// <summary>名稱轉大寫（不分文化）：組織裡不分大小寫不能重複。</summary>
    public static string KeyOf(string name) => name.ToUpperInvariant();

    public static Result<ValidatedSource> Validate(HostSourceInput input, IReadOnlyList<AiModelOption> knownModels)
    {
        ArgumentNullException.ThrowIfNull(input);
        var problems = new List<FailureDetail>();
        var name = CleanName(input.Name);
        if (name is null)
        {
            problems.Add(HostFailures.Invalid(NamePath, $"名稱要是 1～{AiRules.MaxNameLength} 個字，不能有控制字元。"));
        }

        var enabled = CheckModels(input, knownModels, problems);
        var defaultModel = CheckDefaultModel(input, enabled, problems);
        CheckDefaultEffort(input, defaultModel, knownModels, problems);
        CheckLimits(input.Limits ?? new HostSourceLimits(null, null, null), problems);
        if (problems.Count > 0 || name is null)
        {
            return HostFailures.Settings([.. problems]);
        }

        return new ValidatedSource(name, KeyOf(name), enabled, defaultModel, input.DefaultEffort, input.Limits ?? new HostSourceLimits(null, null, null));
    }

    private static List<string> CheckModels(HostSourceInput input, IReadOnlyList<AiModelOption> known, List<FailureDetail> problems)
    {
        var ids = new List<string>();
        foreach (var raw in input.EnabledModels ?? [])
        {
            if (string.IsNullOrEmpty(raw) || raw.Length > AiRules.MaxModelIdLength || raw.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            {
                problems.Add(HostFailures.Invalid(ModelsPath, $"模型代號要是 1～{AiRules.MaxModelIdLength} 個字，不能有空白與控制字元。"));
                continue;
            }

            if (!ids.Contains(raw, StringComparer.Ordinal))
            {
                ids.Add(raw);
            }
        }

        if (ids.Count > AiRules.MaxEnabledModels)
        {
            problems.Add(HostFailures.Invalid(ModelsPath, $"最多開放 {AiRules.MaxEnabledModels} 個模型。"));
        }

        if (input.Enabled && ids.Count == 0)
        {
            problems.Add(HostFailures.Required(ModelsPath, "開著的來源至少要開放一個模型。"));
        }

        // 引擎還沒回報過清單時，關著的來源先存、開著的不行（沒有清單就沒辦法確認模型存在）。
        var checkAgainstList = known.Count > 0 || input.Enabled;
        foreach (var id in ids.Where(id => checkAgainstList && known.All(m => m.Id != id)))
        {
            problems.Add(new FailureDetail(AiErrorCodes.ModelNotAvailable, ModelsPath, $"引擎的模型清單裡沒有「{id}」。"));
        }

        return ids;
    }

    private static string? CheckDefaultModel(HostSourceInput input, List<string> enabled, List<FailureDetail> problems)
    {
        if (input.DefaultModel is null)
        {
            return enabled.FirstOrDefault();
        }

        if (!enabled.Contains(input.DefaultModel, StringComparer.Ordinal))
        {
            problems.Add(new FailureDetail(AiErrorCodes.ModelNotAvailable, DefaultModelPath, "預設的模型要在開放的模型裡。"));
        }

        return input.DefaultModel;
    }

    private static void CheckDefaultEffort(
        HostSourceInput input, string? defaultModel, IReadOnlyList<AiModelOption> known, List<FailureDetail> problems)
    {
        if (input.DefaultEffort is not { } effort)
        {
            return;
        }

        var model = known.FirstOrDefault(m => m.Id == defaultModel);
        if (!Enum.IsDefined(effort) || (model is not null && !model.Efforts.Contains(effort)) || (model is null && known.Count > 0))
        {
            problems.Add(new FailureDetail(AiErrorCodes.ModelNotAvailable, DefaultEffortPath, "預設的思考深度不在那個模型能選的清單裡。"));
        }
    }

    private static void CheckLimits(HostSourceLimits limits, List<FailureDetail> problems)
    {
        if (limits.DailyRequestsPerPerson is { } daily and (< 1 or > AiRules.MaxHostRequestLimit))
        {
            problems.Add(HostFailures.Invalid("limits.dailyRequestsPerPerson", $"每人每天的則數要是 1～{AiRules.MaxHostRequestLimit}，或留空不限。"));
        }

        if (limits.MonthlyRequests is { } monthly and (< 1 or > AiRules.MaxHostRequestLimit))
        {
            problems.Add(HostFailures.Invalid("limits.monthlyRequests", $"每月的則數要是 1～{AiRules.MaxHostRequestLimit}，或留空不限。"));
        }

        if (limits.MonthlyTokens is { } tokens and (< 1 or > AiRules.MaxHostTokenLimit))
        {
            problems.Add(HostFailures.Invalid("limits.monthlyTokens", $"每月的 token 要是 1～{AiRules.MaxHostTokenLimit}，或留空不限。"));
        }
    }
}
