using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Json;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 本機代理登記（<c>POST /v1/agent/check-in</c>）。另外定義而不直接收合約的型別，模組加屬性時對外的內文不會跟著變。
/// <c>capacity</c> 是這台電腦現在還能同時多跑幾件，0 時只登記、不領。
/// </summary>
internal sealed record LocalAgentCheckInRequest(
    string MachineKey, string MachineName, string AgentVersion, IReadOnlyList<LocalEngineRequest?> Engines, int Capacity)
{
    /// <summary>一次最多領幾件的上限：本機代理同時最多跑 2 件，給寬一點，只是擋掉不合理的數字。</summary>
    public const int MaxCapacity = 16;

    /// <summary>陣列裡有 null 的元素時的失敗（要在 <see cref="ToCheckIn"/> 之前檢查）；沒有就是 null。</summary>
    public Failure? FindNullElement() => NullElementChecks.InEngines(Engines, engine => engine.Models, AiErrorCodes.AgentInvalid);

    public LocalAgentCheckIn ToCheckIn() => new(
        MachineKey, MachineName, AgentVersion, [.. Engines.Select(engine => engine!.ToInfo())]);
}

/// <param name="Kind">snake_case：claude_code、codex。</param>
internal sealed record LocalEngineRequest(AiProviderKind Kind, string Version, IReadOnlyList<LocalModelRequest?> Models)
{
    public LocalEngineInfo ToInfo() => new(Kind, Version, [.. Models.Select(model => model!.ToOption())]);
}

/// <param name="Efforts">snake_case：none、low、medium、high、xhigh、max。</param>
/// <param name="DefaultEffort">引擎回報的預設；用字串接，不認得的當 null（不為了它整個請求 400）。</param>
internal sealed record LocalModelRequest(string Id, string DisplayName, IReadOnlyList<AiEffort?> Efforts, string? DefaultEffort = null)
{
    public AiModelOption ToOption() => new(Id, DisplayName, [.. Efforts.Select(effort => effort!.Value)])
    {
        DefaultEffort = DefaultEffort is not null && SnakeCaseEnums.TryParse<AiEffort>(DefaultEffort, out var effort) ? effort : null,
    };
}

internal sealed record LocalAgentCheckInData(LocalAgentJobData? Job)
{
    public static LocalAgentCheckInData From(LocalAgentJob? job) => new(job is null ? null : LocalAgentJobData.From(job));
}

/// <summary>領到的工作。<c>effort</c> 是 null 時不指定。</summary>
internal sealed record LocalAgentJobData(
    Guid JobId,
    AiProviderKind Engine,
    string Model,
    AiEffort? Effort,
    string? ProjectKey,
    string SystemPrompt,
    string Prompt,
    IReadOnlyList<string> AllowedTools,
    string McpUrl,
    int MaxTurns,
    IReadOnlyList<JobAttachmentData> Attachments)
{
    public static LocalAgentJobData From(LocalAgentJob job) => new(
        job.JobId,
        job.Engine,
        job.Model,
        job.Effort,
        job.ProjectKey,
        job.SystemPrompt,
        job.Prompt,
        job.AllowedTools,
        job.McpUrl,
        job.MaxTurns,
        [.. job.Attachments.Select(JobAttachmentData.From)]);
}

/// <summary>
/// 要交給引擎的圖片與 PDF（沒有時是 <c>[]</c>，不省略）。<c>kind</c> 是 <c>image</c> 或 <c>pdf</c>；
/// 用 <c>GET …/jobs/{jobId}/attachments/{id}</c> 下載，核對 <c>sizeBytes</c> 與 <c>sha256</c>。檔名只用來顯示。
/// </summary>
internal sealed record JobAttachmentData(Guid Id, string FileName, string ContentType, AssistantAttachmentKind Kind, long SizeBytes, string Sha256)
{
    public static JobAttachmentData From(LocalAgentJobAttachment attachment) => new(
        attachment.Id, attachment.FileName, attachment.ContentType, attachment.Kind, attachment.SizeBytes, attachment.Sha256);
}

/// <summary>
/// 回報進度的內文。<c>type</c> 是 <c>text</c>（要有 <c>delta</c>）或 <c>tool</c>（要有 <c>callId</c>、<c>name</c>、<c>status</c>）。
/// 用一個形狀而不用多型：屬性的順序不同的內文也收得下，兩種之間不該有的欄位一律拒絕。
/// </summary>
internal sealed record LocalAgentEventsRequest(IReadOnlyList<LocalAgentEventRequest?> Events);

/// <param name="Status">snake_case：running、done、failed。</param>
internal sealed record LocalAgentEventRequest(
    string Type,
    string? Delta = null,
    string? CallId = null,
    string? Name = null,
    AssistantToolStatus? Status = null,
    long? ChangeSetNumber = null,
    string? Error = null,
    Guid? ItemProposalId = null)
{
    /// <summary>
    /// 把一批進度全部轉換；有 null 的元素或內容不合規定時回第一個失敗，路徑是 <c>events[序號]</c>。
    /// 失敗代碼是 <see cref="AiErrorCodes.AgentInvalid"/>，主機代理的端點再換成自己的。
    /// </summary>
    public static Result<IReadOnlyList<LocalAgentEvent>> ConvertAll(IReadOnlyList<LocalAgentEventRequest?> raws)
    {
        var events = new List<LocalAgentEvent>(raws.Count);
        foreach (var raw in raws)
        {
            var path = $"events[{events.Count}]";
            if (raw is null)
            {
                return NullElementChecks.NullAt(path, AiErrorCodes.AgentInvalid);
            }

            var converted = raw.ToEvent(events.Count);
            if (converted.Failure is { } invalid)
            {
                return invalid;
            }

            events.Add(converted.Value);
        }

        return events;
    }

    /// <summary>內容不合 <c>type</c> 的規定時是失敗，路徑是 <c>events[序號]</c> 之下的欄位。</summary>
    public Result<LocalAgentEvent> ToEvent(int index)
    {
        var path = $"events[{index}]";
        switch (Type)
        {
            case "text" when Delta is not null && CallId is null && Name is null && Status is null && ChangeSetNumber is null && Error is null && ItemProposalId is null:
                return new LocalAgentText(Delta);
            case "tool" when CallId is not null && Name is not null && Status is { } status && Delta is null:
                return new LocalAgentTool(new AssistantToolPart(CallId, Name, status, ChangeSetNumber, Error) { ItemProposalId = ItemProposalId });
            case "text":
            case "tool":
                return Invalid(path, $"{Type} 的進度欄位不對：text 只能有 delta，tool 要有 callId、name、status。");
            default:
                return Invalid(path, "type 只能是 text 或 tool。");
        }
    }

    private static Failure Invalid(string path, string message) =>
        new(FailureKind.Invalid, AiErrorCodes.AgentInvalid, message)
        {
            Details = [new FailureDetail(AiErrorCodes.FieldInvalid, path, message)],
        };
}

internal sealed record LocalAgentAckData(bool Cancelled);

/// <param name="Status">snake_case：complete、stopped、failed。</param>
internal sealed record LocalAgentFinishRequest(AssistantMessageStatus Status, string? Error = null, LocalAgentUsageRequest? Usage = null)
{
    public LocalAgentFinish ToFinish() =>
        new(Status, Error, Usage is null ? null : new AssistantUsage(Usage.InputTokens, Usage.OutputTokens));
}

internal sealed record LocalAgentUsageRequest(long InputTokens, long OutputTokens);

/// <summary>「我的帳號」列的一台電腦。</summary>
internal sealed record LocalAgentMachineData(
    Guid Id,
    string Name,
    string AgentVersion,
    IReadOnlyList<LocalAgentEngineData> Engines,
    bool Online,
    DateTimeOffset LastSeenAt)
{
    public static LocalAgentMachineData From(LocalAgentMachine machine) => new(
        machine.Id,
        machine.Name,
        machine.AgentVersion,
        [.. machine.Engines.Select(engine => new LocalAgentEngineData(engine.Kind, engine.Version))],
        machine.Online,
        machine.LastSeenAt);
}

internal sealed record LocalAgentEngineData(AiProviderKind Kind, string Version);

/// <summary>
/// 陣列裡的元素是 null 時，轉換會丟 NullReferenceException 變成 500；先找出來，回和其他欄位一樣的 422，路徑指到那個元素。
/// </summary>
internal static class NullElementChecks
{
    public static Failure NullAt(string path, string code) =>
        new(FailureKind.Invalid, code, $"{path} 不能是 null。")
        {
            Details = [new FailureDetail(AiErrorCodes.FieldInvalid, path, "不能是 null。")],
        };

    /// <summary>檢查 engines、每個引擎的 models、每個模型的 efforts；回第一個 null 的位置。</summary>
    public static Failure? InEngines<TEngine>(
        IReadOnlyList<TEngine?> engines, Func<TEngine, IReadOnlyList<LocalModelRequest?>> modelsOf, string code)
        where TEngine : class
    {
        for (var e = 0; e < engines.Count; e++)
        {
            var enginePath = $"engines[{e}]";
            if (engines[e] is not { } engine)
            {
                return NullAt(enginePath, code);
            }

            var models = modelsOf(engine);
            for (var m = 0; m < models.Count; m++)
            {
                var modelPath = $"{enginePath}.models[{m}]";
                if (models[m] is not { } model)
                {
                    return NullAt(modelPath, code);
                }

                for (var f = 0; f < model.Efforts.Count; f++)
                {
                    if (model.Efforts[f] is null)
                    {
                        return NullAt($"{modelPath}.efforts[{f}]", code);
                    }
                }
            }
        }

        return null;
    }
}
